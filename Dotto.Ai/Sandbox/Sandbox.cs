using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Settings;
using Dotto.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Sandbox;

/// <summary>
/// The single scoped sandbox entity. Encapsulates the whole lifecycle of one session's container:
/// eager host-dir creation (<see cref="InitializeAsync"/>), lazy container start with health
/// polling (<see cref="EnsureStartedAsync"/>), and dispose-driven teardown
/// (stop/remove container, delete host dir).
/// </summary>
internal sealed class Sandbox(IOptions<SandboxOptions> options,
    ILogger<Sandbox> logger,
    IDockerClient client)
    : ISandbox
{
    private const string ContainerLabelKey = "dotto-sandbox";
    private const string ContainerLabelValue = "true";

    private readonly SandboxOptions _options = options.Value;
    private readonly SemaphoreSlim _lifecycleGate = new(1);

    private SandboxMetadata? _metadata;
    private bool _disposed;

    public SandboxMetadata Metadata =>
        _metadata ?? throw new InvalidOperationException("Sandbox is not initialized. Call InitializeAsync first.");

    public SandboxContainer? Container { get; private set; }

    public async Task<SandboxMetadata> InitializeAsync(string sessionId, CancellationToken ct)
    {
        await using var _ = await _lifecycleGate.CaptureWaitAsync(ct);
        ThrowIfDisposed();

        if (_metadata != null)
            return _metadata;

        var hostDir = Path.Combine(_options.HostBasePath, sessionId);
        Directory.CreateDirectory(hostDir);

        // Bot runs as root in its container; sandbox runs as a non-root user.
        // Widen perms on the per-session dir so the sandbox can create its own
        // subdirs (e.g. /work/outputs) for ffmpeg/curl output. Parent dir stays
        // default (root 755). No-op on Windows (dev).
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hostDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }

        _metadata = new SandboxMetadata(sessionId, hostDir);

        logger.LogInformation("Created session directory {Path}", hostDir);
        return _metadata;
    }

    public async Task<SandboxContainer> EnsureStartedAsync(CancellationToken ct)
    {
        await using var _ = await _lifecycleGate.CaptureWaitAsync(ct);
        ThrowIfDisposed();

        if (Container != null)
            return Container;

        var meta = Metadata;
        var containerName = $"sandbox-{meta.SessionId}";

        logger.LogInformation("Creating sandbox container {ContainerName}", containerName);

        var createParams = new CreateContainerParameters
        {
            Image = _options.ImageTag,
            Name = containerName,
            HostConfig = new HostConfig
            {
                // Resource limits
                NanoCPUs = (long)(_options.CpuCount * 1_000_000_000),
                Memory = _options.MemoryMb * 1024 * 1024,
                MemorySwap = _options.MemoryMb * 1024 * 1024, // same as memory (no swap)

                // Hardening
                ReadonlyRootfs = true,
                CapDrop = new List<string> { "ALL" },
                SecurityOpt = new List<string> { "no-new-privileges:true" },

                AutoRemove = true,

                // Bind mount host dir → /work
                Binds =
                [
                    $"{meta.HostSessionDir}:/work"
                ],

                Tmpfs = new Dictionary<string, string>
                {
                    ["/tmp"] = "size=64M"
                },

                // When a Docker network is configured (production: bot runs in a
                // container), connect the sandbox to it. The bot reaches the
                // sandbox by its container name on the shared network — no host
                // port publishing needed.
                //
                // When no network is configured (dev: bot runs on host), publish
                // 8080 to a random host port so the bot reaches it via 127.0.0.1.
                // (PortBindings set below for the dev case only.)
                NetworkMode = _options.NetworkName.IsNullOrWhitespace()
                    ? "default"
                    : _options.NetworkName!,
            },
            Labels = new Dictionary<string, string>
            {
                [ContainerLabelKey] = ContainerLabelValue,
                ["dotto-session-id"] = meta.SessionId
            },
            // Empty environment — no secrets leak into the sandbox
            Env = new List<string>(),
            Cmd = new List<string> { "python3", "/server.py" },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["8080/tcp"] = default
            },
        };

        // Dev mode only (no shared network): publish 8080 to a random host port
        // so the bot can reach the sandbox via 127.0.0.1. In production, the
        // sandbox is reachable by container name on the shared network — no
        // host port needed.
        if (_options.NetworkName.IsNullOrWhitespace())
        {
            createParams.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
            {
                ["8080/tcp"] = [new PortBinding { HostPort = "0" }]
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

        var container = new SandboxContainer(apiUrl, createResponse.ID);
        await WaitForReadyAsync(container, _options.ContainerStartTimeout, ct);

        Container = container;

        logger.LogInformation(
            "Sandbox ready for session {SessionId} at {ApiUrl} (container: {ContainerId})",
            meta.SessionId, apiUrl, createResponse.ID);

        return container;
    }

    /// <summary>
    /// Stops (and, via <c>--rm</c>/AutoRemove, removes) the container if one was
    /// started, then deletes the host session directory. Idempotent.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await using var _ = await _lifecycleGate.CaptureWaitAsync();

        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (Container != null)
                await StopAndRemoveContainerAsync(Container.ContainerId, CancellationToken.None);
        }
        finally
        {
            try
            {
                if (_metadata is not null && Directory.Exists(_metadata.HostSessionDir))
                {
                    Directory.Delete(_metadata.HostSessionDir, recursive: true);
                    logger.LogInformation("Deleted session directory {Path}", _metadata.HostSessionDir);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete session directory {Path}", _metadata?.HostSessionDir);
            }
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, typeof(Sandbox));

    private async Task StopAndRemoveContainerAsync(string containerId, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Stopping sandbox container {ContainerId}", containerId);

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
            logger.LogWarning(
                "Sandbox container {ContainerId} already removed", containerId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to stop sandbox container {ContainerId}", containerId);
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
}
