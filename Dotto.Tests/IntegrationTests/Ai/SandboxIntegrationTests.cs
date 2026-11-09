using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Sandbox;
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
            StorageBasePath = Path.Combine(Path.GetTempPath(), "dotto-sandbox-it-" + Guid.NewGuid().ToString("N")),
            WorkTmpfsMb = 256,
            CpuCount = 1.0,
            MemoryMb = 512,
        };
        var client = new DockerClientBuilder().Build();
        return new Sandbox(Options.Create(opts), NullLogger<Sandbox>.Instance, client);
    }

    [Test]
    public async Task InitializeAsync_CreatesPersistentStorageDirectory()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }

        // Arrange
        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];
        const ulong callerId = 1234;

        try
        {
            // Act
            var meta = await sandbox.InitializeAsync(sessionId, callerId, CancellationToken.None);

            // Assert
            meta.SessionId.ShouldBe(sessionId);
            meta.CallerId.ShouldBe(callerId);
            Directory.Exists(meta.HostStorageDir).ShouldBeTrue();
            meta.HostStorageDir.ShouldEndWith(callerId.ToString());
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
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);

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
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);

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
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
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
    public async Task ContainerApi_RejectsRequestsWithoutToken()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            using var http = new HttpClient();
            using var response = await http.GetAsync(new Uri(container.ApiUrl, "/ping"));

            // Assert
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task FileRoundTrip_StreamsThroughSandboxApi()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            var payload = new byte[] { 1, 2, 3, 4, 5 };
            await using var source = new MemoryStream(payload);
            var path = await container.PutFileAsync("/storage/roundtrip.bin", source, payload.Length, false, CancellationToken.None);

            path.ShouldBe("/storage/roundtrip.bin");

            var file = await container.GetFileAsync(path, CancellationToken.None);
            file.Length.ShouldBe(payload.Length);

            using var sink = new MemoryStream();
            await file.Stream.CopyToAsync(sink, CancellationToken.None);
            await file.Stream.DisposeAsync();

            sink.ToArray().ShouldBe(payload);

            // File must be visible from inside the sandbox as /storage/...
            var ls = await container.ExecuteAsync("test -f /storage/roundtrip.bin && echo present", "/work", "", CancellationToken.None);
            ls.Stdout.ShouldContain("present");
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task GetFileAsync_RejectsSymlinkEscape()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // Plant a symlink inside /storage pointing at the root filesystem.
            var ln = await container.ExecuteAsync("ln -s / /storage/escape", "/work", "", CancellationToken.None);
            ln.ExitCode.ShouldBe(0);

            // Reading through the symlink must be refused by the server.
            var ex = await Should.ThrowAsync<SandboxHttpException>(
                () => container.GetFileAsync("/storage/escape/etc/passwd", CancellationToken.None));

            ex.StatusCode.ShouldBe(400);
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task DisposeAsync_StopsContainerButKeepsPersistentStorage()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];
        string? storageDir;
        string? containerId;
        var options = new SandboxOptions
        {
            ImageTag = SandboxImage,
            ContainerStartTimeout = StartTimeout,
            WallClockTimeout = TimeSpan.FromMinutes(15),
            StorageBasePath = Path.Combine(Path.GetTempPath(), "dotto-sandbox-it-" + Guid.NewGuid().ToString("N")),
            WorkTmpfsMb = 256,
            MemoryMb = 512,
        };

        sandbox = CreateSandbox(options);

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            storageDir = sandbox.Metadata.HostStorageDir;
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);
            containerId = container.ContainerId;

            Directory.Exists(storageDir).ShouldBeTrue();
        }
        finally
        {
            await sandbox.DisposeAsync();
        }

        // Assert — persistent storage survives
        Directory.Exists(storageDir).ShouldBeTrue();

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

        if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        var storageBase = Path.GetDirectoryName(storageDir);
        if (storageBase != null && Directory.Exists(storageBase)) Directory.Delete(storageBase, true);
    }

    [Test]
    public async Task DisposeAsync_WithoutContainerStart_LeavesStorageAlone()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];
        string? storageDir;

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            storageDir = sandbox.Metadata.HostStorageDir;
            Directory.Exists(storageDir).ShouldBeTrue();
            // Don't call EnsureStartedAsync
        }
        finally
        {
            await sandbox.DisposeAsync();
        }

        // Assert
        Directory.Exists(storageDir).ShouldBeTrue();

        if (Directory.Exists(storageDir)) Directory.Delete(storageDir, true);
        var storageBase = Path.GetDirectoryName(storageDir);
        if (storageBase != null && Directory.Exists(storageBase)) Directory.Delete(storageBase, true);
    }
}
