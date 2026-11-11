using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Sandbox;

internal sealed class SandboxReaper(
    IOptions<SandboxOptions> options,
    ILogger<SandboxReaper> logger,
    IDockerClient client)
    : BackgroundService
{
    private const string ContainerLabelFilter = "dotto-sandbox=true";

    private readonly SandboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "SandboxReaper not started: the AI sandbox is disabled (Ai:Sandbox:Enabled=false)");
            return;
        }

        logger.LogInformation(
            "SandboxReaper started (interval: 30s, wall-clock timeout: {Timeout})",
            _options.WallClockTimeout);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "SandboxReaper sweep failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [ContainerLabelFilter] = true
                    },
                    ["status"] = new Dictionary<string, bool>
                    {
                        ["running"] = true
                    }
                }
            },
            ct);

        var now = DateTime.UtcNow;

        foreach (var container in containers)
        {
            var age = now - container.Created;
            if (age <= _options.WallClockTimeout)
                continue;

            container.Labels.TryGetValue("dotto-session-id", out var sessionId);

            logger.LogWarning(
                "Reaping orphan sandbox container {ContainerId} (session: {SessionId}, age: {Age})",
                container.ID, sessionId ?? "unknown", age);

            try
            {
                await client.Containers.StopContainerAsync(
                    container.ID,
                    new ContainerStopParameters
                    {
                        WaitBeforeKillSeconds = 10
                    },
                    ct);

                await client.Containers.RemoveContainerAsync(
                    container.ID,
                    new ContainerRemoveParameters { Force = true },
                    ct);
            }
            catch (DockerContainerNotFoundException)
            {
                // AutoRemove won the race.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stop orphan container {ContainerId}", container.ID);
            }
        }
    }
}
