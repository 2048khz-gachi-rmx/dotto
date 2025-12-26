using System.Text;
using Dotto.Application.Abstractions.MediaProcessing;
using Dotto.Application.Abstractions.Upload;
using Dotto.Application.Models;
using Dotto.Common.Constants;
using Dotto.Common.Exceptions;
using Dotto.Discord.CommandHandlers.Download;
using Dotto.Infrastructure.Downloader.Contracts.Abstractions;
using Dotto.Infrastructure.Downloader.Contracts.Models;
using Dotto.Infrastructure.Downloader.Contracts.Models.Metadata;
using Microsoft.EntityFrameworkCore;
using NetCord.Gateway;
using NetCord.JsonModels;
using NetCord.Rest;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Dotto.Tests.IntegrationTests.CommandHandlers.Download;

public class DownloadCommandHandlerTests : TestDatabaseFixtureBase
{
    private readonly IMediaProcessingService _mockMediaProcessingService = Substitute.For<IMediaProcessingService>();
    private readonly IUploadService _mockUploadService = Substitute.For<IUploadService>();
    
    private DownloadCommandHandler _sut;
    private const ulong TestChannelId = 1234567890;
    private const ulong TestMessageId = 9876543210;
    private const ulong TestInvokerId = 111222333;

    [SetUp]
    public new void Setup()
    {
        _sut = new DownloadCommandHandler(DbContext, _mockMediaProcessingService, TestDateTimeProvider, _mockUploadService);
    }

    [Test]
    public async Task CreateMessage_ShouldCreateMessage_WithAttachedMedia()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");
        var downloadedMedia = new DownloadedMedia
        {
            Video = new MemoryStream(Encoding.UTF8.GetBytes("test video content")),
            FileSize = 1024,
            Number = 1,
            Metadata = new DownloadedMediaMetadata { Title = "Test Video" },
            VideoFormat = new FormatData { Resolution = "1920x1080", VideoCodec = "h264" },
            AudioFormat = null
        };
        
        var downloadedMediaList = new List<DownloadedMedia> { downloadedMedia };
        
        _mockMediaProcessingService.ProcessMediaFromUrlAsync(testUri, Arg.Any<DownloadOptions>())
            .Returns(new MediaDownloadResult()
            {
                Media = downloadedMediaList
            });

        // Act
        var result = await _sut.CreateMessage<InteractionMessageProperties>(testUri, false);

        // Assert
        result.Message.Content.ShouldNotBeNull();
        
        result.AttachedVideos.Count.ShouldBe(1);
        
        var expectedContent = $"-# Test Video.mp4" +
                             $" | 1920x1080" +
                             $" | {downloadedMedia.Video.Length} B" +
                             $" | H264 (AVC)";
        
        result.Message.Content.ShouldBe(expectedContent);
    }

    [Test]
    public async Task CreateMessage_ShouldCreateMessage_WithExternalMedia()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");

        var downloadedMedia = new DownloadedMedia
        {
            Video = new MemoryStream(Encoding.UTF8.GetBytes("test video content")),
            FileSize = 1024 * 1024 * 100, // Large enough to trigger external upload (over 10MB limit)
            Number = 1,
            Metadata = new DownloadedMediaMetadata { Title = "Large Test Video" },
            VideoFormat = new FormatData { Resolution = "1920x1080", VideoCodec = "h264" },
            AudioFormat = null
        };
        
        var downloadedMediaList = new List<DownloadedMedia> { downloadedMedia };
        _mockMediaProcessingService.ProcessMediaFromUrlAsync(testUri, Arg.Any<DownloadOptions>())
            .Returns(new MediaDownloadResult()
            {
                Media = downloadedMediaList
            });
        
        // Mock upload service to return a test URL
        var uploadedUrl = new Uri("https://example.com/uploaded/video.mp4");
        _mockUploadService.UploadFile(
            Arg.Any<Stream>(), 
            Arg.Any<long>(), 
            Arg.Any<string?>(), 
            Arg.Any<string>(), 
            Arg.Any<CancellationToken>())
            .Returns(uploadedUrl);

        // Act
        var result = await _sut.CreateMessage<InteractionMessageProperties>(testUri, false, 10 << 20); // 10MB limit

        // Assert - External media handling
        result.Message.Content.ShouldNotBeNull();
        
        // Check that external videos are added to the message
        result.ExternalVideos.Count.ShouldBe(1);
        result.ExternalVideos.First().ShouldBe(uploadedUrl);
        
        // Verify Discord message formatting is correct for external media
        var expectedContent = $"-# [Large Test Video.mp4](https://example.com/uploaded/video.mp4)" +
                             $" | 1920x1080" +
                             $" | {downloadedMedia.Video.Length} B" +
                             $" | H264 (AVC)";
        
        result.Message.Content.ShouldContain(expectedContent);
    }

    [Test]
    public async Task CreateMessage_ShouldCreateMessage_WithNoMediaFound()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");

        _mockMediaProcessingService.ProcessMediaFromUrlAsync(testUri, Arg.Any<DownloadOptions>())
            .Returns(new MediaDownloadResult()
            {
                Media = []
            });

        // Act
        var result = await _sut.CreateMessage<InteractionMessageProperties>(testUri, false);

        // Assert - No media found case
        result.Message.Content.ShouldNotBeNull();
        result.Message.Content.ShouldBe("No (eligible) videos found or all downloaders failed");
    }

    [Test]
    public async Task LogDownloadedMedia_ShouldSaveRecordsToDatabase()
    {
        // Arrange
        var now = TestDateTimeProvider.SetNow();
        var testUri = new Uri("https://example.com/video.mp4");
        var mockDownloader = Substitute.For<IDownloaderService>();

        var downloadedMedia = new DownloadedMedia
        {
            Video = new MemoryStream(Encoding.UTF8.GetBytes("test video content")),
            FileSize = 1024,
            Number = 1,
            Metadata = new DownloadedMediaMetadata { Title = "Test Video" },
            VideoFormat = new FormatData { Resolution = "1920x1080", VideoCodec = "h264" },
            AudioFormat = null
        };
        
        var downloadedMediaList = new List<DownloadedMedia> { downloadedMedia };
        mockDownloader.Download(testUri, Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(downloadedMediaList.AsReadOnly());
        
        // Create a message with attachments
        var result = await _sut.CreateMessage<InteractionMessageProperties>(testUri, false);
        var restMessage = CreateMessage("aboba", [ new() { Url = "https://video.com" } ]);

        // Act
        await _sut.LogDownloadedMedia(restMessage, result, TestInvokerId);

        // Assert - Database records are saved
        var mediaRecords = await DbContext.DownloadedMedia.ToListAsync();
        mediaRecords.Count.ShouldBe(1);
        
        var record = mediaRecords.Single();
        record.ChannelId.ShouldBe(TestChannelId);
        record.MessageId.ShouldBe(TestMessageId);
        record.InvokerId.ShouldBe(TestInvokerId);
        record.DownloadedFrom.ShouldBe(testUri.ToString());
        record.MediaUrl.ShouldBe("https://video.com");
        record.CreatedOn.ShouldBe(now);
    }

    [Test]
    public async Task CreateMessage_ShouldCreateCorrectDiscordEmbeds_WhenErrorsReturned()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");
        var mockDownloader = Substitute.For<IDownloaderService>();
        
        // Simulate service unavailable exception
        mockDownloader.Download(testUri, Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceUnavailableException("TestService"));
        
        _mockMediaProcessingService.ProcessMediaFromUrlAsync(testUri, Arg.Any<DownloadOptions>())
            .Returns(new MediaDownloadResult()
            {
                Media = [],
                Errors = [
                    new MediaDownloadError()
                    {
                        ErrorCode = MediaErrorCode.ServiceUnavailable,
                        Message = "Downloader Unavailable"
                    },
                    new MediaDownloadError()
                    {
                        ErrorCode = MediaErrorCode.DownloaderError,
                        Message = "Downloader Error"
                    }
                ]
            });

        // Act
        var result = await _sut.CreateMessage<InteractionMessageProperties>(testUri, false);

        result.Message.Content.ShouldBeNull();
        result.Message.Embeds.ShouldNotBeNull();
        result.Message.Embeds.Count().ShouldBe(2);
        
        result.Message.Embeds.ElementAt(0).Color.ShouldBe(Constants.Colors.ErrorColor);
        result.Message.Embeds.ElementAt(0).Title.ShouldBe("Downloader Unavailable");
                
        result.Message.Embeds.ElementAt(1).Color.ShouldBe(Constants.Colors.ErrorColor);
        result.Message.Embeds.ElementAt(1).Title.ShouldBe("Downloader Error");
    }

    [Test]
    public async Task CreateMessage_ShouldCreateCorrectDiscordEmbeds_WhenErrorsReturnedWithMedia()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");
        
        var downloadedMedia = new DownloadedMedia
        {
            Video = new MemoryStream(Encoding.UTF8.GetBytes("test video content")),
            FileSize = 1024,
            Number = 1,
            Metadata = new DownloadedMediaMetadata { Title = "Test Video" },
            VideoFormat = new FormatData { Resolution = "1920x1080", VideoCodec = "h264" },
            AudioFormat = null
        };
        
        _mockMediaProcessingService.ProcessMediaFromUrlAsync(testUri, Arg.Any<DownloadOptions>())
            .Returns(new MediaDownloadResult()
            {
                Media = [ downloadedMedia ],
                Errors = [
                    new MediaDownloadError()
                    {
                        ErrorCode = MediaErrorCode.ServiceUnavailable,
                        Message = "Downloader Unavailable"
                    },
                    new MediaDownloadError()
                    {
                        ErrorCode = MediaErrorCode.DownloaderError,
                        Message = "Downloader Error"
                    }
                ]
            });

        // Act
        var result = await _sut.CreateMessage<InteractionMessageProperties>(testUri, false);

        var expectedContent = $"-# Test Video.mp4" +
                              $" | 1920x1080" +
                              $" | {downloadedMedia.Video.Length} B" +
                              $" | H264 (AVC)";
        
        result.Message.Content.ShouldBe(expectedContent);
        
        result.Message.Embeds.ShouldNotBeNull();
        result.Message.Embeds.Count().ShouldBe(2);
        
        result.Message.Embeds.ElementAt(0).Color.ShouldBe(Constants.Colors.WarningColor);
        result.Message.Embeds.ElementAt(0).Title.ShouldBe("Downloader Unavailable");
                
        result.Message.Embeds.ElementAt(1).Color.ShouldBe(Constants.Colors.WarningColor);
        result.Message.Embeds.ElementAt(1).Title.ShouldBe("Downloader Error");
    }
    
    // https://github.com/NetCordDev/NetCord/blob/alpha/Tests/ServicesTest/CommandServiceTester.cs#L7
    private Message CreateMessage(string content, JsonAttachment[] attachments)
    {
        JsonMessage jsonModel = new()
        {
            Id = TestMessageId,
            ChannelId = TestChannelId,
            Author = new(),
            MentionedUsers = [],
            Attachments = attachments,
            Embeds = [],
            Content = content,
        };

        return new(jsonModel, null, null, NetCordClient.Rest);
    }
}