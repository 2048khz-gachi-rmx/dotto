using System.Reflection;
using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Internal;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

public class SandboxReaperTests
{
    private static SandboxOptions DefaultOptions() => new()
    {
        WallClockTimeout = TimeSpan.FromMinutes(15),
        HostBasePath = Path.GetTempPath()
    };

    private static SandboxReaper CreateReaper(
        IDockerClient client,
        SandboxOptions? options = null)
    {
        var opts = options ?? DefaultOptions();
        var wrapped = Options.Create(opts);
        return new SandboxReaper(wrapped, NullLogger<SandboxReaper>.Instance, client);
    }

    private static ContainerListResponse RunningContainer(
        string id,
        DateTime created,
        string? sessionId = null)
    {
        var labels = new Dictionary<string, string>
        {
            ["dotto-sandbox"] = "true"
        };
        if (sessionId != null)
            labels["dotto-session-id"] = sessionId;

        return new ContainerListResponse
        {
            ID = id,
            Created = created,
            Labels = labels,
            State = "running"
        };
    }

    [Test]
    public async Task SweepAsync_StopsContainerExceedingWallClockTimeout()
    {
        // Arrange
        var client = Substitute.For<IDockerClient>();
        var oldContainer = RunningContainer(
            "abc123",
            DateTime.UtcNow - TimeSpan.FromMinutes(30),
            "session-old");
        client.Containers.ListContainersAsync(
            Arg.Any<ContainersListParameters>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<ContainerListResponse> { oldContainer });

        var reaper = CreateReaper(client);

        // Act — call the private SweepAsync via reflection
        var sweepMethod = typeof(SandboxReaper).GetMethod("SweepAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)sweepMethod!.Invoke(reaper, [CancellationToken.None])!;

        // Assert
        await client.Containers.Received(1).StopContainerAsync(
            "abc123",
            Arg.Any<ContainerStopParameters>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SweepAsync_LeavesRecentContainerRunning()
    {
        // Arrange
        var client = Substitute.For<IDockerClient>();
        var recentContainer = RunningContainer(
            "fresh456",
            DateTime.UtcNow - TimeSpan.FromMinutes(1),
            "session-fresh");
        client.Containers.ListContainersAsync(
            Arg.Any<ContainersListParameters>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<ContainerListResponse> { recentContainer });

        var reaper = CreateReaper(client);

        var sweepMethod = typeof(SandboxReaper).GetMethod("SweepAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)sweepMethod!.Invoke(reaper, [CancellationToken.None])!;

        // Assert
        await client.Containers.DidNotReceive().StopContainerAsync(
            Arg.Any<string>(),
            Arg.Any<ContainerStopParameters>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SweepAsync_DeletesOrphanSessionDirectory()
    {
        // Arrange
        var tempBase = Path.Combine(Path.GetTempPath(), "dotto-reaper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBase);
        var sessionDir = Path.Combine(tempBase, "session-orphan");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "leftover.txt"), "data");

        var client = Substitute.For<IDockerClient>();
        var orphan = RunningContainer(
            "orphan789",
            DateTime.UtcNow - TimeSpan.FromMinutes(30),
            "session-orphan");
        client.Containers.ListContainersAsync(
            Arg.Any<ContainersListParameters>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<ContainerListResponse> { orphan });

        var reaper = CreateReaper(client, new SandboxOptions
        {
            WallClockTimeout = TimeSpan.FromMinutes(15),
            HostBasePath = tempBase
        });

        try
        {
            var sweepMethod = typeof(SandboxReaper).GetMethod("SweepAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            await (Task)sweepMethod!.Invoke(reaper, [CancellationToken.None])!;

            // Assert
            Directory.Exists(sessionDir).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
        }
    }

    [Test]
    public async Task SweepAsync_ContinuesWhenStopFails()
    {
        // Arrange
        var tempBase = Path.Combine(Path.GetTempPath(), "dotto-reaper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBase);
        var sessionDir = Path.Combine(tempBase, "session-fail");
        Directory.CreateDirectory(sessionDir);

        var client = Substitute.For<IDockerClient>();
        var orphan = RunningContainer(
            "fail000",
            DateTime.UtcNow - TimeSpan.FromMinutes(30),
            "session-fail");
        client.Containers.ListContainersAsync(
            Arg.Any<ContainersListParameters>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<ContainerListResponse> { orphan });

        client.Containers.StopContainerAsync(
            "fail000",
            Arg.Any<ContainerStopParameters>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("docker daemon unavailable"));

        var reaper = CreateReaper(client, new SandboxOptions
        {
            WallClockTimeout = TimeSpan.FromMinutes(15),
            HostBasePath = tempBase
        });

        try
        {
            var sweepMethod = typeof(SandboxReaper).GetMethod("SweepAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            // Act — should not throw
            await (Task)sweepMethod!.Invoke(reaper, [CancellationToken.None])!;

            // Assert — the session dir should still be cleaned up despite stop failure
            Directory.Exists(sessionDir).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
        }
    }

    [Test]
    public async Task SweepAsync_HandlesContainerWithoutSessionLabel()
    {
        // Arrange
        var client = Substitute.For<IDockerClient>();
        var unlabeled = RunningContainer(
            "nolabel999",
            DateTime.UtcNow - TimeSpan.FromMinutes(30),
            sessionId: null);
        client.Containers.ListContainersAsync(
            Arg.Any<ContainersListParameters>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<ContainerListResponse> { unlabeled });

        var reaper = CreateReaper(client);

        var sweepMethod = typeof(SandboxReaper).GetMethod("SweepAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        // Act — should not throw
        await (Task)sweepMethod!.Invoke(reaper, [CancellationToken.None])!;

        // Assert — still attempted to stop it
        await client.Containers.Received(1).StopContainerAsync(
            "nolabel999",
            Arg.Any<ContainerStopParameters>(),
            Arg.Any<CancellationToken>());
    }
}
