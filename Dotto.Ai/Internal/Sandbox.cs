using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Abstractions;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Internal;

/// <summary>
/// The single scoped sandbox entity. Encapsulates the whole lifecycle of one session's container:
/// eager host-dir creation (<see cref="InitializeAsync"/>), lazy container start with health
/// polling (<see cref="EnsureStartedAsync"/>), and dispose-driven teardown
/// (stop/remove container, delete host dir).
/// </summary>
internal sealed class Sandbox : ISandbox
{
    private const string ContainerLabelKey = "dotto-sandbox";
    private const string ContainerLabelValue = "true";

    private readonly SandboxOptions _options;
    private readonly ILogger<Sandbox> _logger;
    private readonly IDockerClient _client;

    private SandboxMetadata? _metadata;
    private bool _disposed;

    public Sandbox(IOptions<SandboxOptions> options, ILogger<Sandbox> logger, IDockerClient client)
    {
        _options = options.Value;
        _logger = logger;
        _client = client;
    }

    public SandboxMetadata Metadata =>
        _metadata ?? throw new InvalidOperationException("Sandbox is not initialized. Call InitializeAsync first.");

    public SandboxContainer? Container { get; private set; }

    public Task<SandboxMetadata> InitializeAsync(string sessionId, CancellationToken ct)
    {
        if (_metadata != null)
            return Task.FromResult(_metadata);

        var hostDir = Path.Combine(_options.HostBasePath, sessionId);
        Directory.CreateDirectory(hostDir);
        _metadata = new SandboxMetadata(sessionId, hostDir);

        _logger.LogInformation("Created session directory {Path}", hostDir);
        return Task.FromResult(_metadata);
    }

    public async Task<SandboxContainer> EnsureStartedAsync(CancellationToken ct)
    {
        if (Container != null)
            return Container;

        var meta = Metadata;
        var containerName = $"sandbox-{meta.SessionId}";

        _logger.LogInformation("Creating sandbox container {ContainerName}", containerName);

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

                // Publish container 8080 to a random host port so the bot can
                // reach it via localhost regardless of network topology
                // (Docker Desktop, Podman VM, WSL, etc.).
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["8080/tcp"] = [new PortBinding { HostPort = "0" }]
                },
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

        // Apply gVisor runtime if configured
        if (_options.UseGVisorRuntime)
            createParams.HostConfig.Runtime = "runsc";

        var createResponse = await _client.Containers.CreateContainerAsync(createParams, ct);

        _logger.LogInformation(
            "Created sandbox container {ContainerId} for session {SessionId}",
            createResponse.ID, meta.SessionId);

        await _client.Containers.StartContainerAsync(
            createResponse.ID,
            new ContainerStartParameters(),
            ct);

        _logger.LogInformation(
            "Started sandbox container {ContainerId}", createResponse.ID);

        // Inspect to find the randomly assigned host port
        var apiUrl = await ResolveApiUrlAsync(createResponse.ID, ct);

        var container = new SandboxContainer(apiUrl, createResponse.ID);
        await WaitForReadyAsync(container, _options.ContainerStartTimeout, ct);

        Container = container;

        _logger.LogInformation(
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
                    _logger.LogInformation("Deleted session directory {Path}", _metadata.HostSessionDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete session directory {Path}", _metadata?.HostSessionDir);
            }
        }
    }

    private async Task StopAndRemoveContainerAsync(string containerId, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Stopping sandbox container {ContainerId}", containerId);

            // AutoRemove (-rm) handles removal once the container stops.
            await _client.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters
                {
                    WaitBeforeKillSeconds = 5
                },
                ct);
        }
        catch (DockerContainerNotFoundException)
        {
            _logger.LogWarning(
                "Sandbox container {ContainerId} already removed", containerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
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
                    _logger.LogInformation(
                        "Sandbox ready after {Attempts} attempts ({Url})", attempts, container.ApiUrl);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogTrace(
                    "Sandbox not ready yet (attempt {Attempts}): {Message}",
                    attempts, ex.Message);
            }

            await Task.Delay(delay, cts.Token);
        }

        throw new TimeoutException(
            $"Sandbox at {container.ApiUrl} did not become ready within {timeout}.");
    }

    /// <summary>
    /// Inspects the container to find the randomly assigned host port for 8080/tcp,
    /// then returns <c>http://127.0.0.1:&lt;port&gt;</c>.
    /// </summary>
    private async Task<Uri> ResolveApiUrlAsync(string containerId, CancellationToken ct)
    {
        var inspect = await _client.Containers.InspectContainerAsync(containerId, ct);

        var hostPort = inspect.NetworkSettings?.Ports["8080/tcp"].FirstOrDefault()?.HostPort;

        if (hostPort != null)
        {
            _logger.LogInformation(
                "Sandbox reachable at 127.0.0.1:{Port} (container: {ContainerId})",
                hostPort, containerId);
            return new Uri($"http://127.0.0.1:{hostPort}");
        }

        // Fallback: shouldn't happen since we always publish, but handle gracefully
        _logger.LogWarning(
            "Could not find host port mapping for container {ContainerId}, using loopback fallback",
            containerId);
        return new Uri("http://127.0.0.1:8080");
    }
}
