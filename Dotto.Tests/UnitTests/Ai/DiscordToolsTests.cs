using System.Text;
using Dotto.Ai.Abstractions;
using Dotto.Ai.Internal;
using Dotto.Ai.Models;
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

    private static (DiscordTools tools, IDownloaderServiceFactory factory, ChatAssistantContext context)
        BuildTools(string sessionDir)
    {
        var accessor = new ContextAccessor();
        var context = BuildContext();
        accessor.Set(context);

        var factory = Substitute.For<IDownloaderServiceFactory>();
        var sandbox = Substitute.For<ISandbox>();
        sandbox.Metadata.Returns(new SandboxMetadata("test-session", sessionDir));

        var tools = new DiscordTools(accessor, factory, sandbox);
        return (tools, factory, context);
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
        var (tools, factory, _) = BuildTools(sessionDir);

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
            File.Exists(Path.Combine(sessionDir, "downloads", "video.mp4")).ShouldBeTrue();
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
        var (tools, factory, _) = BuildTools(sessionDir);

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
            File.Exists(Path.Combine(sessionDir, "downloads", "first.mp4")).ShouldBeTrue();
            File.Exists(Path.Combine(sessionDir, "downloads", "second.mp4")).ShouldBeTrue();
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
        var (tools, factory, _) = BuildTools(sessionDir);

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
        var (tools, factory, _) = BuildTools(sessionDir);

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
        Directory.CreateDirectory(sessionDir);
        var downloadsDir = Path.Combine(sessionDir, "downloads");
        Directory.CreateDirectory(downloadsDir);
        // Pre-create a file that would collide
        File.WriteAllText(Path.Combine(downloadsDir, "collide.mp4"), "existing");

        var (tools, factory, _) = BuildTools(sessionDir);
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
            // Both downloaded files are named "collide" but collide.mp4 already exists
            // (from setup), so they get suffixed _1 and _2.
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
        var (tools, _, _) = BuildTools(sessionDir);

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
    public async Task UploadFile_RejectsNonWorkPath()
    {
        var sessionDir = Path.Combine(Path.GetTempPath(), "dotto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        var (tools, _, _) = BuildTools(sessionDir);

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
        var (tools, _, _) = BuildTools(sessionDir);

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
        Directory.CreateDirectory(sessionDir);
        var outputsDir = Path.Combine(sessionDir, "outputs");
        Directory.CreateDirectory(outputsDir);
        var filePath = Path.Combine(outputsDir, "result.mp4");
        await File.WriteAllTextAsync(filePath, "data");

        var (tools, _, context) = BuildTools(sessionDir);

        try
        {
            var result = await tools.UploadFile("/work/outputs/result.mp4");

            result.ShouldContain("Enqueued");
            result.ShouldContain("result.mp4");
            context.Attachments.Count.ShouldBe(1);
            context.Attachments[0].FileName.ShouldBe("result.mp4");
        }
        finally
        {
            await Task.WhenAll(context.Attachments.Select(att => att.Stream.DisposeAsync().AsTask()));
            
            if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true);
        }
    }
}
