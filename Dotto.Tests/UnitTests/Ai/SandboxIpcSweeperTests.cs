using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>The sweep bounds IPC directories by the longest sandbox lifetime.</summary>
public class SandboxIpcSweeperTests
{
    private static string NewBaseDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dotto-sandbox-ipc-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SandboxIpcSweeper Create(string basePath) => new(
        Options.Create(new SandboxOptions
        {
            IpcBasePath = basePath,
            WallClockTimeout = TimeSpan.FromMinutes(15),
            ContainerStartTimeout = TimeSpan.FromSeconds(30),
        }),
        NullLogger<SandboxIpcSweeper>.Instance);

    [Test]
    public async Task StartAsync_RemovesOnlyDirectoriesOlderThanTheSessionLifetime()
    {
        var basePath = NewBaseDir();
        var stale = Path.Combine(basePath, "1_old");
        var live = Path.Combine(basePath, "2_new");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(live);

        // Older than WallClockTimeout + ContainerStartTimeout (15m30s).
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow - TimeSpan.FromHours(2));
        Directory.SetLastWriteTimeUtc(live, DateTime.UtcNow - TimeSpan.FromMinutes(1));

        try
        {
            await Create(basePath).StartAsync(CancellationToken.None);

            Directory.Exists(stale).ShouldBeFalse();
            Directory.Exists(live).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }

    [Test]
    public async Task StartAsync_WhenBaseDirectoryDoesNotExist_DoesNotThrow()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "dotto-sandbox-ipc-missing-" + Guid.NewGuid().ToString("N"));

        await Should.NotThrowAsync(() => Create(basePath).StartAsync(CancellationToken.None));
    }

    [Test]
    public async Task StartAsync_WhenSandboxIsDisabled_LeavesDirectoriesAlone()
    {
        var basePath = NewBaseDir();
        var stale = Path.Combine(basePath, "1_old");
        Directory.CreateDirectory(stale);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow - TimeSpan.FromHours(2));

        var sweeper = new SandboxIpcSweeper(
            Options.Create(new SandboxOptions { Enabled = false, IpcBasePath = basePath }),
            NullLogger<SandboxIpcSweeper>.Instance);

        try
        {
            await sweeper.StartAsync(CancellationToken.None);
            Directory.Exists(stale).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }
}
