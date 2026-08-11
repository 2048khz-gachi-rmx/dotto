using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Settings;
using Dotto.Common;
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
            var created = container.Created;
            var age = now - created;
            container.Labels.TryGetValue("dotto-session-id", out var sessionId);
            sessionId ??= "unknown";

            if (age > _options.WallClockTimeout)
            {
                logger.LogWarning(
                    "Reaping orphan sandbox container {ContainerId} (session: {SessionId}, age: {Age})",
                    container.ID, sessionId, age);

                try
                {
                    await client.Containers.StopContainerAsync(
                        container.ID,
                        new ContainerStopParameters
                        {
                            WaitBeforeKillSeconds = 10
                        },
                        ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to stop orphan container {ContainerId}", container.ID);
                }

                // Remove the orphaned session directory if it exists
                if (!sessionId.IsNullOrWhitespace() && sessionId != "unknown")
                {
                    var hostDir = Path.Combine(_options.HostBasePath, sessionId);
                    try
                    {
                        if (Directory.Exists(hostDir))
                        {
                            Directory.Delete(hostDir, recursive: true);
                            logger.LogInformation(
                                "Deleted orphan session directory {Path}", hostDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "Failed to delete orphan directory {Path}", hostDir);
                    }
                }
            }
        }
    }

}
