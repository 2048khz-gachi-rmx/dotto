using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Settings;
using Dotto.Application.Abstractions.Upload;
using Dotto.Application.Configuration;
using Dotto.Discord.CommandHandlers.Ai;
using Microsoft.EntityFrameworkCore;
using NetCord.Gateway;
using NetCord.JsonModels;
using NetCord.Rest;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.IntegrationTests.CommandHandlers.Ai;

public class AiCommandHandlerMediaLoggingTests : TestDatabaseFixtureBase
{
    private const ulong TestChannelId = 1234567890;
    private const ulong TestMessageId = 9876543210;
    private const ulong TestInvokerId = 111222333;

    private readonly IUploadService _mockUploadService = Substitute.For<IUploadService>();

    private AiCommandHandler _sut;

    [SetUp]
    public new void Setup()
    {
        _sut = new AiCommandHandler(
            chatAssistant: null!,
            sandbox: null!,
            gateway: NetCordClient,
            interactionConfiguration: Substitute.For<IInteractionConfiguration>(),
            dbContext: DbContext,
            dateTimeProvider: TestDateTimeProvider,
            uploadService: _mockUploadService);
    }

    [Test]
    public async Task LogUploadedMedia_ShouldSaveRecordForDiscordDeliveredAttachment()
    {
        // Arrange
        var now = TestDateTimeProvider.SetNow();

        var response = new ChatAssistantResponse
        {
            Response = "here",
            Attachments =
            [
                new AgentAttachment(new MemoryStream(), "a.mp4", 3)
                {
                    SourcePath = "/work/outputs/a.mp4",
                    DiscordAttachment = new AttachmentProperties("a.mp4", new MemoryStream())
                }
            ]
        };

        var sentMessage = CreateMessage("aboba", [new() { Url = "https://cdn.discord/a.mp4" }]);

        // Act
        await _sut.LogUploadedMedia(sentMessage, response, TestInvokerId);

        // Assert
        var records = await DbContext.DownloadedMedia.ToListAsync();
        records.Count.ShouldBe(1);

        var record = records.Single();
        record.ChannelId.ShouldBe(TestChannelId);
        record.MessageId.ShouldBe(TestMessageId);
        record.InvokerId.ShouldBe(TestInvokerId);
        record.DownloadedFrom.ShouldBe("/work/outputs/a.mp4");
        record.MediaUrl.ShouldBe("https://cdn.discord/a.mp4");
        record.CreatedOn.ShouldBe(now);
    }

    [Test]
    public async Task LogUploadedMedia_ShouldSaveRecordsForBothDiscordAndS3DeliveredFiles()
    {
        // Arrange
        var now = TestDateTimeProvider.SetNow();

        var response = new ChatAssistantResponse
        {
            Attachments =
            [
                new AgentAttachment(new MemoryStream(), "a.mp4", 3)
                {
                    SourcePath = "/work/outputs/a.mp4",
                    DiscordAttachment = new AttachmentProperties("a.mp4", new MemoryStream())
                },
                new AgentAttachment(new MemoryStream(), "b.mp4", 3)
                {
                    SourcePath = "/work/outputs/b.mp4",
                    S3AttachmentUrl = new Uri("https://s3.example/b.mp4")
                },
                // Neither delivered anywhere — must not be logged.
                new AgentAttachment(new MemoryStream(), "c.mp4", 3)
            ]
        };

        var sentMessage = CreateMessage("aboba", [new() { Url = "https://cdn.discord/a.mp4" }]);

        // Act
        await _sut.LogUploadedMedia(sentMessage, response, TestInvokerId);

        // Assert
        var records = await DbContext.DownloadedMedia
            .OrderBy(record => record.MediaUrl)
            .ToListAsync();

        records.Count.ShouldBe(2);

        records[0].MediaUrl.ShouldBe("https://cdn.discord/a.mp4");
        records[0].DownloadedFrom.ShouldBe("/work/outputs/a.mp4");
        records[0].CreatedOn.ShouldBe(now);
        records[0].InvokerId.ShouldBe(TestInvokerId);

        records[1].MediaUrl.ShouldBe("https://s3.example/b.mp4");
        records[1].DownloadedFrom.ShouldBe("/work/outputs/b.mp4");
        records[1].CreatedOn.ShouldBe(now);
        records[1].InvokerId.ShouldBe(TestInvokerId);
    }

    [Test]
    public async Task LogUploadedMedia_ShouldNotSaveAnything_WhenNothingWasDelivered()
    {
        // Arrange
        TestDateTimeProvider.SetNow();
        var response = new ChatAssistantResponse
        {
            Response = "no files",
            Attachments = [new AgentAttachment(new MemoryStream(), "c.mp4", 3)]
        };

        var sentMessage = CreateMessage("aboba", []);

        // Act
        await _sut.LogUploadedMedia(sentMessage, response, TestInvokerId);

        // Assert
        (await DbContext.DownloadedMedia.CountAsync()).ShouldBe(0);
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
