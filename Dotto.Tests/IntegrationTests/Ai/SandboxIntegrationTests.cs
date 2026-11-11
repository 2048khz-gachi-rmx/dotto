using System.Net;
using System.Text;
using System.Text.Unicode;
using System.Text.Json;
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

    private const long QuotaMb = 16;
    private const long QuotaBytes = QuotaMb * 1024 * 1024;
    private const long MinFileChargeBytes = 256 * 1024;

    /// <summary>
    /// Options for the quota tests: the option validator requires
    /// <c>MemoryMb &gt; WorkTmpfsMb</c>, and the quota minimum is 16 MB.
    /// </summary>
    private static SandboxOptions CreateQuotaOptions() => new()
    {
        ImageTag = SandboxImage,
        ContainerStartTimeout = StartTimeout,
        WallClockTimeout = TimeSpan.FromMinutes(15),
        StorageBasePath = Path.Combine(Path.GetTempPath(), "dotto-sandbox-it-" + Guid.NewGuid().ToString("N")),
        WorkTmpfsMb = 256,
        CpuCount = 1.0,
        MemoryMb = 512,
        StorageQuotaMb = QuotaMb,
    };

    private static void DeleteStorageBase(string? storageDir)
    {
        if (string.IsNullOrEmpty(storageDir))
            return;

        var baseDir = Path.GetDirectoryName(storageDir);
        try
        {
            if (baseDir != null && Directory.Exists(baseDir))
                Directory.Delete(baseDir, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
    public async Task ExecuteAsync_ChartingLibrariesAreInstalled()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // Act: render a plot and draw text with Pillow, the two things an assistant reaches
            // for when asked to "draw a chart" or "annotate an image".
            const string script = """
                python3 - <<'PY'
                import matplotlib
                matplotlib.use("Agg")
                import matplotlib.pyplot as plt
                from PIL import Image, ImageDraw, ImageFont

                fig, ax = plt.subplots(figsize=(4, 2), dpi=90)
                ax.plot([1, 2, 3], [2, 1, 3])
                fig.savefig("/work/plot.png")
                plt.close(fig)

                font = ImageFont.truetype(
                    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 16)
                image = Image.new("RGB", (120, 40), "white")
                ImageDraw.Draw(image).text((4, 4), "hi", fill="black", font=font)
                image.save("/work/text.png")
                print("rendered")
                PY
                """;

            var result = await container.ExecuteAsync(script, "/work", "", CancellationToken.None);

            // Assert
            result.ExitCode.ShouldBe(0);
            result.Stdout.ShouldContain("rendered");

            var sizes = await container.ExecuteAsync(
                "stat -c %s /work/plot.png /work/text.png", "/work", "", CancellationToken.None);
            sizes.ExitCode.ShouldBe(0);
            sizes.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(long.Parse).ShouldAllBe(size => size > 200);
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task ExecuteAsync_DetachedGrandchildDoesNotHangSession()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // The background child inherits the shell's stdout/stderr pipes and
            // outlives the shell; the server must return after its bounded
            // drain instead of waiting for the pipe to close.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await container.ExecuteAsync(
                "sh -c 'sleep 30' & echo started", "/work", "", CancellationToken.None);
            stopwatch.Stop();

            result.ExitCode.ShouldBe(0);
            result.Stdout.ShouldContain("started");
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task ExecuteAsync_BinaryOutputIsSanitizedNotAnError()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // Writes every byte value 0x00-0xFF: invalid UTF-8 must not fail the
            // request (previously raised UnicodeDecodeError inside communicate).
            var result = await container.ExecuteAsync(
                "python3 -c \"import sys; sys.stdout.buffer.write(bytes(range(256)))\"",
                "/work", "", CancellationToken.None);

            // Assert — sanitized text, not an error response
            result.ExitCode.ShouldBe(0);
            result.Stdout.ShouldNotBeNullOrEmpty();
            result.Stdout.ShouldContain("\uFFFD");
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task ExecuteAsync_HugeOutputReturnsBoundedResponse()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // 5 MB of output: must be drained (child never blocks) while the
            // server only retains a bounded tail.
            var result = await container.ExecuteAsync(
                "head -c 5000000 /dev/zero | tr '\\0' a",
                "/work", "", CancellationToken.None);

            // Assert
            result.ExitCode.ShouldBe(0);
            result.Stdout.Length.ShouldBeLessThanOrEqualTo(10_000);
            result.Stdout.Length.ShouldBeGreaterThan(9_000);
            result.Stdout.ShouldContain("truncated");
        }
        finally
        {
            await sandbox.DisposeAsync();
        }
    }

    [Test]
    public async Task ExecuteAsync_AstralOutputStaysWithinClientBuffer()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // Worst case for JSON: non-ASCII is ASCII-escaped, so each astral
            // character costs 12 bytes in the body ("\ud834\udd1e").
            var result = await container.ExecuteAsync(
                "python3 -c \"print('\\U0001D11E'*40000)\"",
                "/work", "", CancellationToken.None);

            // Assert — request succeeded and the body respects the invariant.
            // HttpClient would have thrown during buffering if it did not.
            result.ExitCode.ShouldBe(0);
            result.Stdout.ShouldContain("\U0001D11E");
            result.Stdout.ShouldContain("truncated");
            // string.Length counts UTF-16 units; the cap is on characters.
            result.Stdout.EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(10_000);

            var body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["exitCode"] = result.ExitCode,
                ["stdout"] = result.Stdout,
                ["stderr"] = result.Stderr,
            });
            var serializedBytes = Encoding.UTF8.GetByteCount(body);

            serializedBytes.ShouldBeLessThanOrEqualTo((int)SandboxContainer.MaxBufferedResponseBytes);
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
    public async Task WorkTmpfs_IsWritableAndExecutable()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await sandbox.InitializeAsync(sessionId, 1, CancellationToken.None);
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // /work is scratch space for the non-root sandbox user: commands must
            // be able to create directories and files under it.
            var write = await container.ExecuteAsync(
                "mkdir -p /work/downloads && echo hi > /work/downloads/a.txt && cat /work/downloads/a.txt",
                "/work", "", CancellationToken.None);
            write.ExitCode.ShouldBe(0);
            write.Stdout.ShouldContain("hi");

            // DownloadMedia streams every downloaded file into /work/downloads.
            var payload = new byte[] { 1, 2, 3, 4, 5 };
            await using var source = new MemoryStream(payload);
            var path = await container.PutFileAsync(
                "/work/downloads/roundtrip.bin", source, payload.Length, true, CancellationToken.None);

            path.ShouldBe("/work/downloads/roundtrip.bin");

            var file = await container.GetFileAsync(path, CancellationToken.None);
            file.Length.ShouldBe(payload.Length);

            using var sink = new MemoryStream();
            await file.Stream.CopyToAsync(sink, CancellationToken.None);
            await file.Stream.DisposeAsync();
            sink.ToArray().ShouldBe(payload);

            // A script written into /work must be directly executable, not only
            // runnable through an interpreter (tmpfs is noexec by default).
            var script = await container.ExecuteAsync(
                "printf '#!/bin/sh\\necho ran\\n' > /work/t.sh && chmod +x /work/t.sh && /work/t.sh",
                "/work", "", CancellationToken.None);
            script.ExitCode.ShouldBe(0);
            script.Stdout.ShouldContain("ran");
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
    public async Task PutFile_OverQuota_Returns507AndLeavesNoFile()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox(CreateQuotaOptions());
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];
        string? storageDir = null;

        try
        {
            await sandbox.InitializeAsync(sessionId, 7001, CancellationToken.None);
            storageDir = sandbox.Metadata.HostStorageDir;
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // Leave exactly one minimum charge of headroom (256 KiB), then try
            // to write a body that needs more than that.
            var fillBytes = QuotaBytes - MinFileChargeBytes;
            await using (var fill = new MemoryStream(new byte[fillBytes]))
            {
                await container.PutFileAsync("/storage/fill.bin", fill, fillBytes, false, CancellationToken.None);
            }

            var rejectedBytes = MinFileChargeBytes * 2;
            await using var rejected = new MemoryStream(new byte[rejectedBytes]);

            // Act
            var ex = await Should.ThrowAsync<SandboxHttpException>(
                () => container.PutFileAsync("/storage/rejected.bin", rejected, rejectedBytes, false, CancellationToken.None));

            // Assert — 507 and no trace of the rejected file (not even a .part)
            ex.StatusCode.ShouldBe(507);

            var present = await container.ExecuteAsync(
                "test -f /storage/fill.bin && echo fill-present", "/work", "", CancellationToken.None);
            present.Stdout.ShouldContain("fill-present");

            var ls = await container.ExecuteAsync("ls -A /storage", "/work", "", CancellationToken.None);
            ls.Stdout.ShouldNotContain("rejected");
        }
        finally
        {
            await sandbox.DisposeAsync();
            DeleteStorageBase(storageDir);
        }
    }

    [Test]
    public async Task PutFile_ManyTinyFiles_TripMinimumPerFileCharge()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox(CreateQuotaOptions());
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];
        string? storageDir = null;

        try
        {
            await sandbox.InitializeAsync(sessionId, 7002, CancellationToken.None);
            storageDir = sandbox.Metadata.HostStorageDir;
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            // A 16 MB quota at a 256 KiB minimum charge fits exactly 64 files.
            var expectedSuccesses = QuotaBytes / MinFileChargeBytes;
            var successes = 0;
            SandboxHttpException? rejection = null;

            for (var i = 0; i < 70 && rejection == null; i++)
            {
                var payload = new byte[] { (byte)(i % 256) };
                await using var source = new MemoryStream(payload);
                try
                {
                    await container.PutFileAsync($"/storage/tiny-{i}.bin", source, payload.Length, false, CancellationToken.None);
                    successes++;
                }
                catch (SandboxHttpException ex)
                {
                    rejection = ex;
                }
            }

            // Assert — the quota trips on the 65th one-byte file.
            rejection.ShouldNotBeNull();
            rejection.StatusCode.ShouldBe(507);
            successes.ShouldBe((int)expectedSuccesses);

            // ...even though the real bytes on disk are a rounding error.
            var usage = await container.ExecuteAsync(
                "find /storage -type f -printf '%s\\n' | awk '{s+=$1} END {print s+0}'",
                "/work", "", CancellationToken.None);
            usage.ExitCode.ShouldBe(0);
            long.Parse(usage.Stdout.Trim()).ShouldBeLessThan(QuotaBytes / 100);
        }
        finally
        {
            await sandbox.DisposeAsync();
            DeleteStorageBase(storageDir);
        }
    }

    [Test]
    public async Task ExecuteAsync_StorageQuotaBreach_KillsCommand()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox(CreateQuotaOptions());
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];
        string? storageDir = null;

        try
        {
            await sandbox.InitializeAsync(sessionId, 7003, CancellationToken.None);
            storageDir = sandbox.Metadata.HostStorageDir;
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Writes 64 MB into a 16 MB quota, then stays alive long enough for
            // the watchdog's one-second poll to observe the breach.
            var result = await container.ExecuteAsync(
                "dd if=/dev/zero of=/storage/fill.bin bs=1M count=64 2>/dev/null; sleep 30",
                "/work", "short", CancellationToken.None);

            stopwatch.Stop();

            // Assert — killed and reported as a quota breach, not a timeout.
            result.ExitCode.ShouldBe(-1);
            result.Stderr.ShouldContain("Storage quota exceeded");
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await sandbox.DisposeAsync();
            DeleteStorageBase(storageDir);
        }
    }

    [Test]
    public async Task ContainerApi_UnauthorizedResponseClosesConnection()
    {
        if (!IsRuntimeAvailable()) { Assert.Ignore("Container runtime not available"); }
        if (!IsImagePresent()) { Assert.Ignore($"{SandboxImage} image not built"); }

        var sandbox = CreateSandbox();
        var sessionId = "it-" + Guid.NewGuid().ToString("N")[..8];
        string? storageDir = null;

        try
        {
            await sandbox.InitializeAsync(sessionId, 7004, CancellationToken.None);
            storageDir = sandbox.Metadata.HostStorageDir;
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);

            using var http = new HttpClient();

            // Wrong token. The unread body of a rejected PUT would be parsed as
            // the next request line on a kept-alive HTTP/1.1 socket, so the
            // rejection must close it.
            using var wrongRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(container.ApiUrl, "/ping"));
            wrongRequest.Headers.Add("X-Sandbox-Token", "wrong-token");
            using (var wrongResponse = await http.SendAsync(wrongRequest))
            {
                wrongResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
                wrongResponse.Headers.ConnectionClose.ShouldBe(true);
            }

            // Missing token.
            using var missingResponse = await http.GetAsync(new Uri(container.ApiUrl, "/ping"));
            missingResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            missingResponse.Headers.ConnectionClose.ShouldBe(true);
        }
        finally
        {
            await sandbox.DisposeAsync();
            DeleteStorageBase(storageDir);
        }
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
