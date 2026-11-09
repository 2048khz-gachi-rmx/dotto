using System.Text;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Tools;
using Dotto.Application.Abstractions.Factories;
using Dotto.Infrastructure.Downloader.Contracts.Abstractions;
using Dotto.Infrastructure.Downloader.Contracts.Models;
using Dotto.Infrastructure.Downloader.Contracts.Models.Metadata;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

public class DiscordToolsTests
{
    private static ChatAssistantContext BuildContext()
    {
        var ctx = new ChatAssistantContext
        {
            CallerId = 123,
            CallerName = "tester",
            ChannelId = 456,
            GuildId = 789,
        };
        return ctx;
    }

    private static (DiscordTools tools, IDownloaderServiceFactory factory, ChatAssistantContext context, string sessionDir)
        BuildTools(string sessionDir)
    {
        var accessor = new ContextAccessor();
        var context = BuildContext();
        accessor.Set(context);

        var factory = Substitute.For<IDownloaderServiceFactory>();
        var container = new FakeSandboxContainer(sessionDir);
        var sandbox = Substitute.For<ISandbox>();
        sandbox.Metadata.Returns(new SandboxMetadata("test-session", context.CallerId, sessionDir));
        sandbox.EnsureStartedAsync(Arg.Any<CancellationToken>()).Returns(container);

        var tools = new DiscordTools(accessor, factory, sandbox);
        return (tools, factory, context, sessionDir);
    }

    private static DownloadedMedia BuildMedia(string title = "video", string ext = "mp4", string content = "fake")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new DownloadedMedia
        {
            Video = stream,
            FileSize = bytes.Length,
            Number = 1,
            Metadata = new DownloadedMediaMetadata { Title = title },
            VideoFormat = new FormatData { Extension = ext, VideoCodec = "h264" },
            AudioFormat = null
        };
    }

    [Test]
    public async Task DownloadMedia_WritesFileToDownloadsDir()
    {
        // Arrange
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        var (tools, factory, _, _) = BuildTools(sessionDir);

        var downloader = Substitute.For<IDownloaderService>();
        var media = BuildMedia();
        downloader.Download(Arg.Any<string>(), Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<DownloadedMedia> { media });
        factory.CreateDownloaderService(Arg.Any<string>()).Returns(new[] { downloader });

        try
        {
            // Act
            var result = await tools.DownloadMedia("https://example.com/video.mp4");

            // Assert
            result.ShouldContain("/work/downloads/");
            result.ShouldContain("video.mp4");
            File.Exists(Path.Combine(sessionDir, "work", "downloads", "video.mp4")).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }

    [Test]
    public async Task DownloadMedia_AllFilesFromMultipleResults()
    {
        // Arrange
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        var (tools, factory, _, _) = BuildTools(sessionDir);

        var media1 = BuildMedia("first");
        var media2 = BuildMedia("second");
        var downloader = Substitute.For<IDownloaderService>();
        downloader.Download(Arg.Any<string>(), Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<DownloadedMedia> { media1, media2 });
        factory.CreateDownloaderService(Arg.Any<string>()).Returns(new[] { downloader });

        try
        {
            // Act
            var result = await tools.DownloadMedia("https://example.com/playlist");

            // Assert
            result.ShouldContain("2 files");
            result.ShouldContain("/work/downloads/first.mp4");
            result.ShouldContain("/work/downloads/second.mp4");
            File.Exists(Path.Combine(sessionDir, "work", "downloads", "first.mp4")).ShouldBeTrue();
            File.Exists(Path.Combine(sessionDir, "work", "downloads", "second.mp4")).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }

    [Test]
    public async Task DownloadMedia_FallsBackToNextDownloader()
    {
        // Arrange
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        var (tools, factory, _, _) = BuildTools(sessionDir);

        var failing = Substitute.For<IDownloaderService>();
        failing.Download(Arg.Any<string>(), Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("yt-dlp failed"));

        var success = Substitute.For<IDownloaderService>();
        success.Download(Arg.Any<string>(), Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<DownloadedMedia> { BuildMedia("recovered") });

        // First downloader fails, second succeeds
        factory.CreateDownloaderService(Arg.Any<string>()).Returns(new[] { failing, success });

        try
        {
            // Act
            var result = await tools.DownloadMedia("https://example.com/video.mp4");

            // Assert
            result.ShouldContain("/work/downloads/recovered.mp4");
        }
        finally
        {
            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }

    [Test]
    public async Task DownloadMedia_ReturnsFailure_WhenAllDownloadersEmpty()
    {
        // Arrange
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        var (tools, factory, _, _) = BuildTools(sessionDir);

        var downloader = Substitute.For<IDownloaderService>();
        downloader.Download(Arg.Any<string>(), Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<DownloadedMedia>());
        factory.CreateDownloaderService(Arg.Any<string>()).Returns(new[] { downloader });

        try
        {
            // Act
            var result = await tools.DownloadMedia("https://example.com/notfound");

            // Assert
            result.ShouldContain("Failed");
        }
        finally
        {
            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }

    [Test]
    public async Task DownloadMedia_AvoidsFilenameCollision()
    {
        // Arrange
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        var downloadsDir = Path.Combine(sessionDir, "work", "downloads");
        Directory.CreateDirectory(downloadsDir);
        await File.WriteAllTextAsync(Path.Combine(downloadsDir, "collide.mp4"), "existing");

        var (tools, factory, _, _) = BuildTools(sessionDir);
        var media1 = BuildMedia("collide");
        var media2 = BuildMedia("collide");
        var downloader = Substitute.For<IDownloaderService>();
        downloader.Download(Arg.Any<string>(), Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<DownloadedMedia> { media1, media2 });
        factory.CreateDownloaderService(Arg.Any<string>()).Returns(new[] { downloader });

        try
        {
            // Act
            var result = await tools.DownloadMedia("https://example.com/dupe");

            // Assert
            // Both downloaded files are named "collide" but collide.mp4 already
            // exists, so the sandbox server suffixes them _1 and _2.
            result.ShouldContain("2 files");
            result.ShouldContain("/work/downloads/collide_1.mp4");
            result.ShouldContain("/work/downloads/collide_2.mp4");
            File.Exists(Path.Combine(downloadsDir, "collide.mp4")).ShouldBeTrue();
            File.Exists(Path.Combine(downloadsDir, "collide_1.mp4")).ShouldBeTrue();
            File.Exists(Path.Combine(downloadsDir, "collide_2.mp4")).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }

    [Test]
    public async Task UploadFile_RejectsPathTraversal()
    {
        // Arrange
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        var (tools, _, _, _) = BuildTools(sessionDir);

        try
        {
            // Act
            var result = await tools.UploadFile("/work/../../etc/passwd");

            // Assert
            result.ShouldContain("Invalid");
        }
        finally
        {
            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }

    [Test]
    public async Task UploadFile_RejectsNonSandboxPath()
    {
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        var (tools, _, _, _) = BuildTools(sessionDir);

        try
        {
            var result = await tools.UploadFile("/etc/passwd");
            result.ShouldContain("Invalid");
        }
        finally
        {
            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }

    [Test]
    public async Task UploadFile_RejectsMissingFile()
    {
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        var (tools, _, _, _) = BuildTools(sessionDir);

        try
        {
            var result = await tools.UploadFile("/work/outputs/nonexistent.mp4");
            result.ShouldContain("not found");
        }
        finally
        {
            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }

    [Test]
    public async Task UploadFile_EnqueuesExistingFile()
    {
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        var outputsDir = Path.Combine(sessionDir, "work", "outputs");
        Directory.CreateDirectory(outputsDir);
        var filePath = Path.Combine(outputsDir, "result.mp4");
        await File.WriteAllTextAsync(filePath, "data");

        var (tools, _, context, _) = BuildTools(sessionDir);

        try
        {
            var result = await tools.UploadFile("/work/outputs/result.mp4");

            result.ShouldContain("Enqueued");
            result.ShouldContain("result.mp4");
            context.Attachments.Count.ShouldBe(1);
            context.Attachments[0].FileName.ShouldBe("result.mp4");
            context.Attachments[0].Length.ShouldBe(4);

            using var reader = new StreamReader(context.Attachments[0].Stream, Encoding.UTF8, leaveOpen: true);
            (await reader.ReadToEndAsync()).ShouldBe("data");
        }
        finally
        {
            await Task.WhenAll(context.Attachments.Select(att => att.Stream.DisposeAsync().AsTask()));

            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }

    /// <summary>
    /// Stands in for the sandbox HTTP server: maps sandbox paths under a test
    /// root and mimics the server's dedupe + not-found behavior.
    /// </summary>
    private sealed class FakeSandboxContainer(string root) : ISandboxContainer
    {
        public string ContainerId => "fake-container";
        public Uri ApiUrl { get; } = new("http://127.0.0.1:1");

        public Task<bool> PingAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<SandboxExecuteResult> ExecuteAsync(string command, string cwd = "", string timeout = "", CancellationToken ct = default)
            => Task.FromResult(new SandboxExecuteResult(0, string.Empty, string.Empty));

        public Task<SandboxFile> GetFileAsync(string sandboxPath, CancellationToken ct)
        {
            var path = Map(sandboxPath);
            if (!File.Exists(path))
                throw new SandboxHttpException(404, "not found");

            var length = new FileInfo(path).Length;
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult(new SandboxFile(stream, length));
        }

        public async Task<string> PutFileAsync(string sandboxPath, Stream content, long length, bool dedupe, CancellationToken ct)
        {
            var path = Map(sandboxPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (dedupe && File.Exists(path))
            {
                var stem = Path.Combine(Path.GetDirectoryName(path)!,
                    Path.GetFileNameWithoutExtension(path));
                var ext = Path.GetExtension(path);
                var index = 1;
                while (File.Exists($"{stem}_{index}{ext}"))
                    index++;
                path = $"{stem}_{index}{ext}";
            }

            await using (var sink = File.Create(path))
                await content.CopyToAsync(sink, ct);

            return "/" + Path.GetRelativePath(root, path).Replace('\\', '/');
        }

        private string Map(string sandboxPath)
        {
            if (!sandboxPath.StartsWith('/'))
                throw new SandboxHttpException(400, "invalid path");
            return Path.Combine(root, sandboxPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
