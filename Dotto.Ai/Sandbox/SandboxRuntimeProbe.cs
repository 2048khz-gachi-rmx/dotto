using Docker.DotNet;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Sandbox;

/// <summary>Logs when gVisor is enabled but <c>runsc</c> is not registered; never blocks startup.</summary>
internal sealed class SandboxRuntimeProbe(
    IOptions<SandboxOptions> options,
    ILogger<SandboxRuntimeProbe> logger,
    IDockerClient client) : IHostedService
{
    private const string RuntimeName = "runsc";

    /// <summary>Keeps an unresponsive daemon from delaying host startup.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly SandboxOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            logger.LogDebug(
                "Sandbox runtime probe skipped: the AI sandbox is disabled (Ai:Sandbox:Enabled=false)");
            return;
        }

        if (!_options.UseGVisorRuntime)
        {
            logger.LogInformation(
                "Sandbox runtime probe: gVisor is disabled, sandboxes use the daemon's default runtime");
            return;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ProbeTimeout);

            var info = await client.System.GetSystemInfoAsync(cts.Token);
            var runtimes = info.Runtimes?.Keys.ToList() ?? [];

            if (runtimes.Contains(RuntimeName, StringComparer.Ordinal))
            {
                logger.LogInformation(
                    "Sandbox runtime probe: {Runtime} is registered (daemon runtimes: {Runtimes})",
                    RuntimeName, string.Join(", ", runtimes));
                return;
            }

            logger.LogError(
                "gVisor is enabled (Ai:Sandbox:UseGVisorRuntime=true) but the Docker daemon has no " +
                "'{Runtime}' runtime (available: {Runtimes}). Install gVisor, register it with " +
                "`runsc install -- --host-uds=create`, restart Docker, then confirm `docker info` " +
                "lists {Runtime} (see .docker/README.md). Sandbox-backed tools fail to start until " +
                "then; set Ai:Sandbox:UseGVisorRuntime=false to run on runc instead.",
                RuntimeName,
                runtimes.Count == 0 ? "none" : string.Join(", ", runtimes),
                RuntimeName);
        }
        catch (Exception ex)
        {
            // Startup must survive an unreachable daemon; tools surface it later.
            logger.LogError(
                ex, "Sandbox runtime probe failed: could not read the Docker daemon's runtime list");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
