using System.Text;
using Dotto.Application.Abstractions.Factories;
using Dotto.Application.InternalServices.UploadService;
using Dotto.Common.Exceptions;
using Dotto.Discord.CommandHandlers.Download;
using Dotto.Infrastructure.Downloader.Contracts.Interfaces;
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
    private readonly IDownloaderServiceFactory _mockDownloaderFactory = Substitute.For<IDownloaderServiceFactory>();
    private readonly IUploadService _mockUploadService = Substitute.For<IUploadService>();
    
    private DownloadCommandHandler _sut;
    private const ulong TestChannelId = 1234567890;
    private const ulong TestMessageId = 9876543210;
    private const ulong TestInvokerId = 111222333;

    [SetUp]
    public new void Setup()
    {
        _sut = new DownloadCommandHandler(DbContext, _mockDownloaderFactory, TestDateTimeProvider, _mockUploadService);
    }

    [Test]
    public async Task CreateMessage_ShouldCreateMessage_WithAttachedMedia()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");
        var mockDownloader = Substitute.For<IDownloaderService>();
        
        var mediaStream = new MemoryStream(Encoding.UTF8.GetBytes("test video content"));
        var downloadedMedia = new DownloadedMedia
        {
            Video = mediaStream,
            FileSize = 1024,
            Number = 1,
            Metadata = new DownloadedMediaMetadata { Title = "Test Video" },
            VideoFormat = new FormatData { Resolution = "1920x1080", VideoCodec = "h264" },
            AudioFormat = null
        };
        
        var downloadedMediaList = new List<DownloadedMedia> { downloadedMedia };
        mockDownloader.Download(testUri, Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(downloadedMediaList);
        
        _mockDownloaderFactory.CreateDownloaderService(testUri).Returns(new[] { mockDownloader });

        // Act
        var result = await _sut.CreateMessage<InteractionMessageProperties>(testUri, false);

        // Assert - Message content and structure
        result.Message.Content.ShouldNotBeNull();
        
        // Check that attachments are added to the message
        result.AttachedVideos.Count.ShouldBe(1);
        
        // Verify Discord message formatting is correct
        var expectedContent = $"-# Test Video.mp4" +
                             $" | 1920x1080" +
                             $" | {mediaStream.Length} B" +
                             $" | H264 (AVC)";
        
        result.Message.Content.ShouldBe(expectedContent);
    }

    [Test]
    public async Task CreateMessage_ShouldCreateMessage_WithExternalMedia()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");
        var mockDownloader = Substitute.For<IDownloaderService>();
        
        var mediaStream = new MemoryStream(Encoding.UTF8.GetBytes("test video content"));
        var downloadedMedia = new DownloadedMedia
        {
            Video = mediaStream,
            FileSize = 1024 * 1024 * 100, // Large enough to trigger external upload (over 10MB limit)
            Number = 1,
            Metadata = new DownloadedMediaMetadata { Title = "Large Test Video" },
            VideoFormat = new FormatData { Resolution = "1920x1080", VideoCodec = "h264" },
            AudioFormat = null
        };
        
        var downloadedMediaList = new List<DownloadedMedia> { downloadedMedia };
        mockDownloader.Download(testUri, Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(downloadedMediaList);
        
        _mockDownloaderFactory.CreateDownloaderService(testUri).Returns(new[] { mockDownloader });
        
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
                             $" | {mediaStream.Length} B" +
                             $" | H264 (AVC)";
        
        result.Message.Content.ShouldContain(expectedContent);
    }

    [Test]
    public async Task CreateMessage_ShouldCreateMessage_WithNoMediaFound()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");
        var mockDownloader = Substitute.For<IDownloaderService>();
        
        var downloadedMediaList = new List<DownloadedMedia>(); // Empty list
        mockDownloader.Download(testUri, Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(downloadedMediaList);
        
        _mockDownloaderFactory.CreateDownloaderService(testUri).Returns(new[] { mockDownloader });

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
        
        var mediaStream = new MemoryStream(Encoding.UTF8.GetBytes("test video content"));
        var downloadedMedia = new DownloadedMedia
        {
            Video = mediaStream,
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
        await _sut.LogDownloadedMedia(restMessage, result, TestInvokerId, testUri);

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
    public async Task CreateMessage_ShouldCreateCorrectDiscordEmbeds_WhenDownloaderUnavailable()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");
        var mockDownloader = Substitute.For<IDownloaderService>();
        
        // Simulate service unavailable exception
        mockDownloader.Download(testUri, Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceUnavailableException("TestService"));
        
        _mockDownloaderFactory.CreateDownloaderService(testUri).Returns(new[] { mockDownloader });

        // Act
        var result = await _sut.CreateMessage<InteractionMessageProperties>(testUri, false);

        // Assert - Embeds should be created for service unavailability
        result.Message.Embeds.ShouldNotBeNull();
        result.Message.Embeds.Count().ShouldBe(1);
        
        var embed = result.Message.Embeds.Single();
        embed.Color.Red.ShouldBe((byte)235);
        embed.Color.Green.ShouldBe((byte)175);
        embed.Color.Blue.ShouldBe((byte)40);
        embed.Description.ShouldNotBeNull().ShouldContain("TestService");
    }

    [Test]
    public async Task CreateMessage_ShouldCreateCorrectDiscordEmbeds_WhenDownloadFails()
    {
        // Arrange
        var testUri = new Uri("https://example.com/video.mp4");
        var mockDownloader = Substitute.For<IDownloaderService>();
        
        // Simulate download failure with exception
        mockDownloader.Download(testUri, Arg.Any<DownloadOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApplicationException("Download failed", new Exception("Inner error details")));
        
        _mockDownloaderFactory.CreateDownloaderService(testUri).Returns(new[] { mockDownloader });

        // Act
        var result = await _sut.CreateMessage<InteractionMessageProperties>(testUri, false);

        // Assert - Embeds should be created for download failure
        result.Message.Embeds.ShouldNotBeNull();
        result.Message.Embeds.Count().ShouldBe(1);
        
        var embed = result.Message.Embeds.Single();
        embed.Color.Red.ShouldBe((byte)140);
        embed.Color.Green.ShouldBe((byte)55);
        embed.Color.Blue.ShouldBe((byte)55);
        embed.Title.ShouldNotBeNull().ShouldContain("Download failed");
        embed.Description.ShouldNotBeNull().ShouldContain("Inner error details");
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