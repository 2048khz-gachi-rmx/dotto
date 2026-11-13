using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Settings;
using Dotto.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Sandbox;

/// <summary>
/// Scoped sandbox for one invocation: storage provisioning, lazy container start, dispose-driven teardown.
/// </summary>
internal sealed class Sandbox(IOptions<SandboxOptions> options,
    ILogger<Sandbox> logger,
    IDockerClient client)
    : ISandbox, IDisposable
{
    private const string ContainerLabelKey = "dotto-sandbox";
    private const string ContainerLabelValue = "true";

    /// <summary>Mount point for the per-session socket directory.</summary>
    private const string SocketMountPath = "/run/dotto";

    private const string SocketFileName = "sandbox.sock";

    private const string ResolvConfFileName = "resolv.conf";

    /// <summary>Serializes writes to the shared resolver file across sessions.</summary>
    private static readonly object ResolvConfLock = new();

    /// <summary>AF_UNIX <c>sun_path</c> is 108 bytes including the NUL.</summary>
    private const int MaxSocketPathBytes = 107;

    /// <summary>
    /// Serializes storage-directory creation per caller across concurrent sessions.
    /// </summary>
    private static readonly ConcurrentDictionary<ulong, SemaphoreSlim> CallerGates = new();

    private readonly SandboxOptions _options = options.Value;
    private readonly SemaphoreSlim _lifecycleGate = new(1);

    private SandboxMetadata? _metadata;
    private string? _token;
    private string? _containerId;
    private string? _ipcDir;
    private bool _disposed;

    /// <summary>
    /// Test seam for the readiness probe; production uses <see cref="SandboxContainer"/>.
    /// </summary>
    internal Func<SandboxEndpoint, string, string, ISandboxContainer> ContainerFactory { get; init; }
        = static (endpoint, containerId, token) => new SandboxContainer(endpoint, containerId, token);

    /// <summary>Named network means per-session socket; otherwise loopback TCP.</summary>
    private bool UseUnixSocket => !_options.NetworkName.IsNullOrWhitespace();

    public SandboxMetadata Metadata
    {
        get
        {
            ThrowIfDisabled();
            return _metadata
                ?? throw new InvalidOperationException("Sandbox is not initialized. Call InitializeAsync first.");
        }
    }

    public ISandboxContainer? Container { get; private set; }

    public async Task<SandboxMetadata> InitializeAsync(string sessionId, ulong callerId, CancellationToken ct)
    {
        ThrowIfDisabled();

        await using var _ = await _lifecycleGate.CaptureWaitAsync(ct);
        ThrowIfDisposed();

        if (_metadata != null)
            return _metadata;

        var gate = CallerGates.GetOrAdd(callerId, static _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try
        {
            var storageDir = Path.Combine(_options.StorageBasePath, callerId.ToString());
            Directory.CreateDirectory(storageDir);

            // Bot is root, sandbox is non-root: widen perms so the sandbox can write here.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(storageDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            }

            _metadata = new SandboxMetadata(sessionId, callerId, storageDir);
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation(
            "Initialized session {SessionId} for caller {CallerId} (storage: {Path})",
            sessionId, callerId, _metadata.HostStorageDir);
        return _metadata;
    }

    public async Task<ISandboxContainer> EnsureStartedAsync(CancellationToken ct)
    {
        ThrowIfDisabled();

        await using var _ = await _lifecycleGate.CaptureWaitAsync(ct);
        ThrowIfDisposed();

        if (Container != null)
            return Container;

        var meta = Metadata;

        var containerName = $"sandbox-{meta.SessionId}-{RandomHex(4)}";
        _token = RandomHex(32);

        // Create the socket directory before the non-root sandbox user starts.
        _ipcDir = UseUnixSocket ? PrepareIpcDirectory(meta.SessionId) : null;

        var binds = new List<string> { $"{meta.HostStorageDir}:/storage" };
        if (_ipcDir != null)
        {
            binds.Add($"{_ipcDir}:{SocketMountPath}");

            // Docker user-defined networks point /etc/resolv.conf at 127.0.0.11,
            // which gVisor's isolated network stack cannot reach. Bind one shared
            // static file instead (gVisor FAQ: Docker user-defined bridge DNS).
            if (_options.DnsServers.Count > 0)
                binds.Add($"{EnsureSharedResolvConf()}:/etc/resolv.conf:ro");
        }

        var env = new List<string>
        {
            $"SANDBOX_TOKEN={_token}",
            $"SANDBOX_STORAGE_QUOTA_MB={_options.StorageQuotaMb}"
        };
        if (_ipcDir != null)
            env.Add($"SANDBOX_SOCKET={SocketMountPath}/{SocketFileName}");

        logger.LogInformation(
            "Creating sandbox container {ContainerName} ({Transport})",
            containerName, _ipcDir == null ? "loopback TCP" : "unix socket");

        var createParams = new CreateContainerParameters
        {
            Image = _options.ImageTag,
            Name = containerName,
            HostConfig = new HostConfig
            {
                NanoCPUs = (long)(_options.CpuCount * 1_000_000_000),
                Memory = _options.MemoryMb * 1024 * 1024,
                MemorySwap = _options.MemoryMb * 1024 * 1024, // same as memory (no swap)
                PidsLimit = 256,

                // Hardening
                ReadonlyRootfs = true,
                CapDrop = new List<string> { "ALL" },
                SecurityOpt = new List<string> { "no-new-privileges:true" },

                DNS = _options.DnsServers,

                AutoRemove = true,

                // The only host paths the sandbox sees; the bot never opens files in them.
                Binds = binds,

                // Docker copies the image directory's owner/mode onto the tmpfs root and
                // mounts tmpfs noexec by default. Without explicit options, `WORKDIR /work`
                // (root:root, 2755) leaves /work unwritable for the non-root sandbox user.
                Tmpfs = new Dictionary<string, string>
                {
                    ["/work"] = $"size={_options.WorkTmpfsMb}M,mode=1777,exec",
                    ["/tmp"] = "size=64M"
                },

                // Production joins the shared network; dev publishes 8080 to loopback only.
                NetworkMode = _options.NetworkName.IsNullOrWhitespace()
                    ? "default"
                    : _options.NetworkName!,
            },
            Labels = new Dictionary<string, string>
            {
                [ContainerLabelKey] = ContainerLabelValue,
                ["dotto-session-id"] = meta.SessionId
            },
            Env = env,
            Cmd = new List<string> { "python3", "/server.py" },
        };

        if (_ipcDir == null)
        {
            // Development: loopback-published TCP port (the network path).
            createParams.ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["8080/tcp"] = default
            };
            createParams.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
            {
                ["8080/tcp"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = "0" }]
            };
        }

        // Apply gVisor runtime if configured
        if (_options.UseGVisorRuntime)
            createParams.HostConfig.Runtime = "runsc";

        string? createdId = null;
        try
        {
            var createResponse = await client.Containers.CreateContainerAsync(createParams, ct);
            createdId = createResponse.ID;

            // Recorded before start/endpoint/readiness so disposal can stop a half-made container.
            _containerId = createResponse.ID;

            logger.LogInformation(
                "Created sandbox container {ContainerId} for session {SessionId}",
                createResponse.ID, meta.SessionId);

            await client.Containers.StartContainerAsync(
                createResponse.ID,
                new ContainerStartParameters(),
                ct);

            logger.LogInformation(
                "Started sandbox container {ContainerId}", createResponse.ID);

            var endpoint = await ResolveEndpointAsync(createResponse.ID, ct);

            var container = ContainerFactory(endpoint, createResponse.ID, _token);
            await WaitForReadyAsync(container, _options.ContainerStartTimeout, ct);

            Container = container;

            logger.LogInformation(
                "Sandbox ready for session {SessionId} at {Endpoint} (container: {ContainerId})",
                meta.SessionId, endpoint, createResponse.ID);

            return container;
        }
        catch
        {
            // Stop the container here: this path throws, so dispose may never run.
            if (createdId != null)
                await TryStopContainerAsync(createdId);
            RemoveIpcDirectory();
            throw;
        }
    }

    /// <summary>Stops the container and removes this session's IPC directory. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        await using var _ = await _lifecycleGate.CaptureWaitAsync();

        if (_disposed)
            return;

        _disposed = true;

        var containerId = Container?.ContainerId ?? _containerId;
        if (containerId != null)
        {
            try
            {
                await StopAndRemoveContainerAsync(containerId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stop sandbox container {ContainerId}", containerId);
            }
        }

        RemoveIpcDirectory();
    }

    /// <summary>
    /// Blocking fallback for synchronously-disposed scopes.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, typeof(Sandbox));

    private void ThrowIfDisabled()
    {
        if (!_options.Enabled)
            throw new SandboxDisabledException();
    }

    /// <summary>Creates the per-session IPC directory, mode 0777 for the non-root sandbox user.</summary>
    private string PrepareIpcDirectory(string sessionId)
    {
        var dir = Path.Combine(_options.IpcBasePath, sessionId);
        var socketPath = Path.Combine(dir, SocketFileName);

        // Fail before create instead of getting ENAMETOOLONG inside the sandbox.
        if (Encoding.UTF8.GetByteCount(socketPath) > MaxSocketPathBytes)
        {
            throw new InvalidOperationException(
                $"Sandbox IPC socket path is too long for AF_UNIX ({socketPath}). " +
                "Shorten Ai:Sandbox:IpcBasePath.");
        }

        Directory.CreateDirectory(dir);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }

        return dir;
    }

    /// <summary>
    /// Writes the shared resolver file outside the per-session directories, so the
    /// sandbox can read it but not rewrite it through the /run/dotto mount.
    /// </summary>
    private string EnsureSharedResolvConf()
    {
        var path = Path.Combine(_options.IpcBasePath, ResolvConfFileName);
        var lines = new List<string>();
        foreach (var server in _options.DnsServers)
            lines.Add($"nameserver {server}");
        var content = string.Join('\n', lines) + "\n";

        lock (ResolvConfLock)
        {
            Directory.CreateDirectory(_options.IpcBasePath);
            if (!File.Exists(path) || File.ReadAllText(path) != content)
                File.WriteAllText(path, content);
        }

        return path;
    }

    /// <summary>Removes the IPC directory; never throws.</summary>
    private void RemoveIpcDirectory()
    {
        var dir = _ipcDir;
        _ipcDir = null;

        if (dir == null)
            return;

        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove sandbox IPC directory {Path}", dir);
        }
    }

    /// <summary>
    /// Never throws: the original failure is already propagating.
    /// </summary>
    private async Task TryStopContainerAsync(string containerId)
    {
        try
        {
            // Not the caller's ct: cleanup must run even when that ct is what failed.
            await StopAndRemoveContainerAsync(containerId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clean up unusable sandbox container {ContainerId}", containerId);
        }
    }

    private async Task StopAndRemoveContainerAsync(string containerId, CancellationToken ct)
    {
        logger.LogInformation("Stopping sandbox container {ContainerId}", containerId);

        try
        {
            // AutoRemove (-rm) handles removal once the container stops.
            await client.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters
                {
                    WaitBeforeKillSeconds = 5
                },
                ct);
        }
        catch (DockerContainerNotFoundException)
        {
            logger.LogWarning("Sandbox container {ContainerId} already removed", containerId);
        }
        catch (Exception ex)
        {
            // Already-gone containers are logged, not surfaced.
            logger.LogWarning(ex, "Failed to stop sandbox container {ContainerId}", containerId);
        }
    }

    private async Task WaitForReadyAsync(ISandboxContainer container, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var delay = TimeSpan.FromMilliseconds(200);
        var attempts = 0;

        while (!cts.Token.IsCancellationRequested)
        {
            attempts++;
            try
            {
                if (await container.PingAsync(cts.Token))
                {
                    logger.LogInformation(
                        "Sandbox ready after {Attempts} attempts ({Endpoint})", attempts, container.Endpoint);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogTrace(
                    "Sandbox not ready yet (attempt {Attempts}): {Message}",
                    attempts, ex.Message);
            }

            await Task.Delay(delay, cts.Token);
        }

        throw new TimeoutException(
            $"Sandbox at {container.Endpoint} did not become ready within {timeout}.");
    }

    /// <summary>Session socket path in production, or the loopback port in development.</summary>
    private async Task<SandboxEndpoint> ResolveEndpointAsync(string containerId, CancellationToken ct)
    {
        if (_ipcDir != null)
        {
            var socketPath = Path.Combine(_ipcDir, SocketFileName);
            logger.LogInformation(
                "Sandbox reachable at unix socket {SocketPath} via {MountPath} (container: {ContainerId})",
                socketPath, SocketMountPath, containerId);
            return SandboxEndpoint.FromSocketPath(socketPath);
        }

        var hostPort = await ResolveHostPortAsync(containerId, ct);

        if (hostPort != null)
        {
            logger.LogInformation(
                "Sandbox reachable at 127.0.0.1:{Port} (container: {ContainerId})",
                hostPort, containerId);
            return SandboxEndpoint.FromUri(new Uri($"http://127.0.0.1:{hostPort}"));
        }

        // Unreachable in practice: 8080 is always published in dev.
        logger.LogWarning(
            "Could not find host port mapping for container {ContainerId}, using loopback fallback",
            containerId);
        return SandboxEndpoint.FromUri(new Uri("http://127.0.0.1:8080"));
    }

    private async Task<string?> ResolveHostPortAsync(string containerId, CancellationToken ct)
    {
        var inspect = await client.Containers.InspectContainerAsync(containerId, ct);
        return inspect.NetworkSettings?.Ports["8080/tcp"].FirstOrDefault()?.HostPort;
    }

    private static string RandomHex(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
