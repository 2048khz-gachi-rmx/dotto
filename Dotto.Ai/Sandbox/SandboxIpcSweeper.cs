using Dotto.Ai.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Sandbox;

/// <summary>Removes IPC directories older than the longest possible sandbox lifetime.</summary>
internal sealed class SandboxIpcSweeper(
    IOptions<SandboxOptions> options,
    ILogger<SandboxIpcSweeper> logger) : IHostedService
{
    private readonly SandboxOptions _options = options.Value;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            logger.LogDebug(
                "Sandbox IPC sweep skipped: the AI sandbox is disabled (Ai:Sandbox:Enabled=false)");
            return Task.CompletedTask;
        }

        Sweep();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal void Sweep()
    {
        var basePath = _options.IpcBasePath;

        if (!Directory.Exists(basePath))
            return;

        var cutoff = DateTime.UtcNow - (_options.WallClockTimeout + _options.ContainerStartTimeout);
        var removed = 0;

        foreach (var directory in Directory.EnumerateDirectories(basePath))
        {
            try
            {
                var lastWrite = Directory.GetLastWriteTimeUtc(directory);
                if (lastWrite > cutoff)
                    continue;

                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to remove stale sandbox IPC directory {Path}", directory);
            }
        }

        if (removed > 0)
        {
            logger.LogInformation(
                "Removed {Count} stale sandbox IPC director{s} from {Path}",
                removed, removed == 1 ? "y" : "ies", basePath);
        }
    }
}
