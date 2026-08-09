using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Internal;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dotto.Tests.IntegrationTests.Ai;

/// <summary>
/// Integration tests for the sandbox container lifecycle. These spin up a real
/// container via the Docker API (Podman is compatible). Requires the
/// dotto-sandbox:latest image to be built and a running container runtime.
///
/// Marked [Explicit] so they don't run in CI — run manually with:
///   dotnet test --filter "FullyQualifiedName~SandboxIntegrationTests"
/// </summary>
[Explicit]
[Category("Integration")]
[Parallelizable(ParallelScope.Children)]
public class SandboxIntegrationTests
{
    private const string SandboxImage = "dotto-sandbox:latest";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    private static bool IsRuntimeAvailable()
    {
        try
        {
            var client = new DockerClientBuilder().Build();
            client.System.GetSystemInfoAsync().GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsImagePresent()
    {
        try
        {
            var client = new DockerClientBuilder().Build();
            var images = client.Images.ListImagesAsync(new ImagesListParameters()).GetAwaiter().GetResult();
            return images.Any(i => i.RepoTags?.Any(t => t.Contains("dotto-sandbox")) == true);
        }
        catch
        {
            return false;
        }
    }

    private static Sandbox CreateSandbox(SandboxOptions? options = null)
    {
        var opts = options ?? new SandboxOptions
        {
            ImageTag = SandboxImage,
            ContainerStartTimeout = StartTimeout,
            WallClockTimeout = TimeSpan.FromMinutes(15),
            HostBasePath = Path.Combine(Path.GetTempPath(), "dotto-sandbox-it-" + Guid.NewGuid().ToString("N")),
            CpuCount = 1.0,
            MemoryMb = 512,
        };
        var client = new DockerClientBuilder().Build();
        return new Sandbox(Options.Create(opts), NullLogger<Sandbox>.Instance, client);
    }

    [Test]
    public async Task InitializeAsync_CreatesHostDirectory()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }

        // Arrange
        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            // Act
            var meta = await sandbox.InitializeAsync(sessionId, CancellationToken.None);

            // Assert
            meta.SessionId.ShouldBe(sessionId);
            Directory.Exists(meta.HostSessionDir).ShouldBeTrue();
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task EnsureStartedAsync_CreatesAndStartsContainer()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, CancellationToken.None);

            // Act
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // Assert
            container.ContainerId.ShouldNotBeNullOrEmpty();
            container.ApiUrl.ShouldNotBeNull();

            // The container should respond to /ping
            var ready = await container.PingAsync(CancellationToken.None);
            ready.ShouldBeTrue();
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task EnsureStartedAsync_SecondCallReusesSameContainer()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, CancellationToken.None);

            var first = await sandbox.EnsureStartedAsync(CancellationToken.None);
            var second = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // Assert — same container instance reused
            second.ContainerId.ShouldBe(first.ContainerId);
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task ExecuteAsync_RunsCommandAndReturnsOutput()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // Act
            var result = await container.ExecuteAsync("echo hello", "/work", "", CancellationToken.None);

            // Assert
            result.ExitCode.ShouldBe(0);
            result.Stdout.ShouldContain("hello");
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task DisposeAsync_StopsContainerAndDeletesHostDir()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];
        string? hostDir;
        string? containerId;

        try
        {
            await sandbox.InitializeAsync(sessionId, CancellationToken.None);
            hostDir = sandbox.Metadata.HostSessionDir;
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);
            containerId = container.ContainerId;

            Directory.Exists(hostDir).ShouldBeTrue();
        }
        finally
        {
            await sandbox.DisposeAsync();
        }

        // Assert — host dir deleted
        Directory.Exists(hostDir).ShouldBeFalse();

        // Assert — container removed (auto-remove via --rm)
        // Poll for removal instead of a fixed delay
        var client = new DockerClientBuilder().Build();
        var removed = false;
        for (var i = 0; i < 50; i++)
        {
            try
            {
                await client.Containers.InspectContainerAsync(containerId, CancellationToken.None);
                await Task.Delay(100);
            }
            catch (DockerContainerNotFoundException)
            {
                removed = true;
                break;
            }
        }
        removed.ShouldBeTrue("Container should have been auto-removed after stop");
    }

    [Test]
    public async Task DisposeAsync_WithoutContainerStart_DeletesHostDirOnly()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];
        string? hostDir;

        try
        {
            await sandbox.InitializeAsync(sessionId, CancellationToken.None);
            hostDir = sandbox.Metadata.HostSessionDir;
            Directory.Exists(hostDir).ShouldBeTrue();
            // Don't call EnsureStartedAsync
        }
        finally
        {
            await sandbox.DisposeAsync();
        }

        // Assert
        Directory.Exists(hostDir).ShouldBeFalse();
    }
}
