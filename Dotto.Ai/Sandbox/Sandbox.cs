using System.Collections.Concurrent;
using System.Security.Cryptography;
using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Settings;
using Dotto.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Sandbox;

/// <summary>
/// The single scoped sandbox entity. Encapsulates the whole lifecycle of one session's container:
/// eager storage provisioning (<see cref="InitializeAsync"/>), lazy container start with health
/// polling (<see cref="EnsureStartedAsync"/>), and dispose-driven teardown (stop/remove container).
/// Persistent storage is never touched by the bot.
/// </summary>
internal sealed class Sandbox(IOptions<SandboxOptions> options,
    ILogger<Sandbox> logger,
    IDockerClient client)
    : ISandbox, IDisposable
{
    private const string ContainerLabelKey = "dotto-sandbox";
    private const string ContainerLabelValue = "true";

    /// <summary>
    /// Serializes storage provisioning for a given caller. Two sessions for the
    /// same user must not race on creating their shared storage directory.
    /// </summary>
    private static readonly ConcurrentDictionary<ulong, SemaphoreSlim> CallerGates = new();

    private readonly SandboxOptions _options = options.Value;
    private readonly SemaphoreSlim _lifecycleGate = new(1);

    private SandboxMetadata? _metadata;
    private string? _token;
    private bool _disposed;

    public SandboxMetadata Metadata =>
        _metadata ?? throw new InvalidOperationException("Sandbox is not initialized. Call InitializeAsync first.");

    public ISandboxContainer? Container { get; private set; }

    public async Task<SandboxMetadata> InitializeAsync(string sessionId, ulong callerId, CancellationToken ct)
    {
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

            // Bot runs as root in its container; sandbox runs as a non-root user.
            // Widen perms so the sandbox can create its own files. The parent
            // stays default (root 755). No-op on Windows (dev).
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
        await using var _ = await _lifecycleGate.CaptureWaitAsync(ct);
        ThrowIfDisposed();

        if (Container != null)
            return Container;

        var meta = Metadata;

        // Random suffix avoids collisions with a container from a previous
        // session with the same second-resolution ID, and a fresh token per
        // container makes the API useless to anything that can merely reach it.
        var containerName = $"sandbox-{meta.SessionId}-{RandomHex(4)}";
        _token = RandomHex(32);

        logger.LogInformation("Creating sandbox container {ContainerName}", containerName);

        var createParams = new CreateContainerParameters
        {
            Image = _options.ImageTag,
            Name = containerName,
            HostConfig = new HostConfig
            {
                // Resource limits. tmpfs pages count against the memory cgroup,
                // so MemoryMb must exceed WorkTmpfsMb.
                NanoCPUs = (long)(_options.CpuCount * 1_000_000_000),
                Memory = _options.MemoryMb * 1024 * 1024,
                MemorySwap = _options.MemoryMb * 1024 * 1024, // same as memory (no swap)
                PidsLimit = 256,

                // Hardening
                ReadonlyRootfs = true,
                CapDrop = new List<string> { "ALL" },
                SecurityOpt = new List<string> { "no-new-privileges:true" },

                AutoRemove = true,

                // Persistent per-user storage. This is the only host path the
                // sandbox sees, and it is a directory the bot never reads or
                // writes — only the sandbox HTTP API moves bytes through it.
                Binds =
                [
                    $"{meta.HostStorageDir}:/storage"
                ],

                // /work is scratch space: ephemeral and capped by the kernel.
                Tmpfs = new Dictionary<string, string>
                {
                    ["/work"] = $"size={_options.WorkTmpfsMb}M",
                    ["/tmp"] = "size=64M"
                },

                // When a Docker network is configured (production: bot runs in a
                // container), connect the sandbox to it. The bot reaches the
                // sandbox by its container name on the shared network — no host
                // port publishing needed.
                //
                // When no network is configured (dev: bot runs on host), publish
                // 8080 to a random loopback port so the bot reaches it via
                // 127.0.0.1. Never bind the sandbox API to all interfaces.
                NetworkMode = _options.NetworkName.IsNullOrWhitespace()
                    ? "default"
                    : _options.NetworkName!,
            },
            Labels = new Dictionary<string, string>
            {
                [ContainerLabelKey] = ContainerLabelValue,
                ["dotto-session-id"] = meta.SessionId
            },
            // No secrets (or anything else) leak into the sandbox: the token is
            // the only environment entry, and it is not a secret for anyone who
            // cannot already reach the container network.
            Env = new List<string>
            {
                $"SANDBOX_TOKEN={_token}",
                $"SANDBOX_STORAGE_QUOTA_MB={_options.StorageQuotaMb}"
            },
            Cmd = new List<string> { "python3", "/server.py" },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["8080/tcp"] = default
            },
        };

        // Dev mode only (no shared network): publish 8080 to a random loopback
        // port. In production, the sandbox is reachable by container name on
        // the shared network — no host port needed.
        if (_options.NetworkName.IsNullOrWhitespace())
        {
            createParams.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
            {
                ["8080/tcp"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = "0" }]
            };
        }

        // Apply gVisor runtime if configured
        if (_options.UseGVisorRuntime)
            createParams.HostConfig.Runtime = "runsc";

        var createResponse = await client.Containers.CreateContainerAsync(createParams, ct);

        logger.LogInformation(
            "Created sandbox container {ContainerId} for session {SessionId}",
            createResponse.ID, meta.SessionId);

        await client.Containers.StartContainerAsync(
            createResponse.ID,
            new ContainerStartParameters(),
            ct);

        logger.LogInformation(
            "Started sandbox container {ContainerId}", createResponse.ID);

        // Inspect to find the sandbox API URL (network-resolved name or host port)
        var apiUrl = await ResolveApiUrlAsync(createResponse.ID, containerName, ct);

        var container = new SandboxContainer(apiUrl, createResponse.ID, _token);
        await WaitForReadyAsync(container, _options.ContainerStartTimeout, ct);

        Container = container;

        logger.LogInformation(
            "Sandbox ready for session {SessionId} at {ApiUrl} (container: {ContainerId})",
            meta.SessionId, apiUrl, createResponse.ID);

        return container;
    }

    /// <summary>
    /// Stops (and, via AutoRemove, removes) the container if one was started.
    /// Idempotent. Persistent storage is deliberately left alone.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await using var _ = await _lifecycleGate.CaptureWaitAsync();

        if (_disposed)
            return;

        _disposed = true;

        if (Container == null)
            return;

        try
        {
            await StopAndRemoveContainerAsync(Container.ContainerId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stop sandbox container {ContainerId}", Container.ContainerId);
        }
    }

    /// <summary>
    /// Synchronous fallback for DI scopes that are disposed synchronously. The
    /// async path is preferred; this blocks only while an existing stop call
    /// completes.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, typeof(Sandbox));

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
    }

    private async Task WaitForReadyAsync(SandboxContainer container, TimeSpan timeout, CancellationToken ct)
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
                        "Sandbox ready after {Attempts} attempts ({Url})", attempts, container.ApiUrl);
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
            $"Sandbox at {container.ApiUrl} did not become ready within {timeout}.");
    }

    /// <summary>
    /// Resolves the URL the bot uses to reach the sandbox HTTP server.
    ///
    /// If a Docker network is configured (production: bot and sandbox are on
    /// the same network), the URL is <c>http://&lt;container-name&gt;:8080</c> —
    /// Docker's embedded DNS resolves the container name.
    ///
    /// Otherwise (dev: bot runs on the host), inspects the container for the
    /// randomly assigned host port and returns <c>http://127.0.0.1:&lt;port&gt;</c>.
    /// </summary>
    private async Task<Uri> ResolveApiUrlAsync(string containerId, string containerName, CancellationToken ct)
    {
        if (!_options.NetworkName.IsNullOrWhitespace())
        {
            var url = new Uri($"http://{containerName}:8080");
            logger.LogInformation(
                "Sandbox reachable at {Url} via network {Network} (container: {ContainerId})",
                url, _options.NetworkName, containerId);
            return url;
        }

        var inspect = await client.Containers.InspectContainerAsync(containerId, ct);

        var hostPort = inspect.NetworkSettings?.Ports["8080/tcp"].FirstOrDefault()?.HostPort;

        if (hostPort != null)
        {
            logger.LogInformation(
                "Sandbox reachable at 127.0.0.1:{Port} (container: {ContainerId})",
                hostPort, containerId);
            return new Uri($"http://127.0.0.1:{hostPort}");
        }

        // Fallback: shouldn't happen since we always publish, but handle gracefully
        logger.LogWarning(
            "Could not find host port mapping for container {ContainerId}, using loopback fallback",
            containerId);
        return new Uri("http://127.0.0.1:8080");
    }

    private static string RandomHex(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
