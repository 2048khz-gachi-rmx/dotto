using System.Text;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Dotto.Application.Abstractions.Upload;
using Dotto.Application.Configuration;
using Dotto.Discord.CommandHandlers.Ai;
using Dotto.Tests.Mocks;
using NetCord;
using NetCord.Gateway;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

public class AiCommandHandlerAttachmentTests
{
    private const long DiscordLimit = 10 << 20;
    private const long S3Limit = 100 << 20;
    private const ulong SelfId = 4242;

    private static readonly GatewayClient Gateway =
        new(new BotToken("ODAzMzc3MjcwODc4MTA5NzI2.tHis.IS.not.A.ReAl.tOkeN"));

    private static AiSettings BuildSettings(int maxAttachments) => new()
    {
        BaseUrl = new Uri("http://localhost:1234"),
        ApiKey = "test-key",
        AssistantModel = "test-model",
        ChatAssistant = new ChatAssistantSettings { MaxAttachments = maxAttachments }
    };

    private static AiCommandHandler BuildHandler(
        int maxAttachments,
        IUploadService? uploadService = null,
        ISandbox? sandbox = null)
        => new(
            chatAssistant: null!,
            sandbox: sandbox ?? new RecordingSandbox(),
            gateway: Gateway,
            interactionConfiguration: Substitute.For<IInteractionConfiguration>(),
            dbContext: null!,
            dateTimeProvider: null!,
            uploadService: uploadService);

    private static AgentAttachment Attachment(string fileName, long length, string? sourcePath = null)
        => new(new MemoryStream(Encoding.UTF8.GetBytes("x")), fileName, length)
        {
            SourcePath = sourcePath
        };

    [Test]
    public async Task MaterializeAttachments_ShouldDropAttachmentsBeyondMax_AndDisposeTheirStreams()
    {
        // Arrange
        var handler = BuildHandler(maxAttachments: 2);
        var attachments = new[]
        {
            Attachment("a.txt", 10),
            Attachment("b.txt", 10),
            Attachment("c.txt", 10),
            Attachment("d.txt", 10),
        };
        var response = new ChatAssistantResponse
        {
            Response = "here you go",
            Attachments = [..attachments]
        };

        // Act
        var result = await handler.MaterializeAttachmentsAsync(response, 2, DiscordLimit, CancellationToken.None);

        // Assert
        result.OmittedCount.ShouldBe(2);
        result.OversizedCount.ShouldBe(0);
        result.MaxAttachments.ShouldBe(2);

        response.Attachments.Count.ShouldBe(2);
        attachments[2].Stream.CanRead.ShouldBeFalse();
        attachments[3].Stream.CanRead.ShouldBeFalse();

        // Kept attachments were materialized into bot-owned temp streams.
        response.Attachments.ShouldAllBe(att => att.DiscordAttachment != null);
        attachments[0].Stream.CanRead.ShouldBeFalse();
        attachments[1].Stream.CanRead.ShouldBeFalse();
    }

    [Test]
    public async Task MaterializeAttachments_ShouldNoteOmittedCount_InResponseText()
    {
        // Arrange
        var handler = BuildHandler(maxAttachments: 1);
        var response = new ChatAssistantResponse
        {
            Response = "here you go",
            Attachments = [Attachment("a.txt", 10), Attachment("b.txt", 10), Attachment("c.txt", 10)]
        };

        // Act
        var result = await handler.MaterializeAttachmentsAsync(response, 1, DiscordLimit, CancellationToken.None);
        AiCommandHandler.ApplyAttachmentNotes(response, result);

        // Assert
        response.Response.ShouldBe("here you go\n\n2 attachment(s) omitted (limit is 1 per response).");
    }

    [Test]
    public async Task MaterializeAttachments_ShouldUploadToS3_WhenAboveDiscordLimitButWithinS3Max()
    {
        // Arrange
        var uploadedUrl = new Uri("https://s3.example/dotto/result.mp4");
        var uploadService = Substitute.For<IUploadService>();
        uploadService.MaxFileSizeBytes.Returns(S3Limit);
        uploadService.UploadFile(
                Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<string?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(uploadedUrl);

        var handler = BuildHandler(maxAttachments: 5, uploadService);
        var attachment = Attachment("result.mp4", 50 << 20, "/work/outputs/result.mp4");
        var response = new ChatAssistantResponse { Attachments = [attachment] };

        // Act
        var result = await handler.MaterializeAttachmentsAsync(response, 5, DiscordLimit, CancellationToken.None);

        // Assert
        result.OversizedCount.ShouldBe(0);
        result.OmittedCount.ShouldBe(0);

        attachment.S3AttachmentUrl.ShouldBe(uploadedUrl);
        attachment.DiscordAttachment.ShouldBeNull();
        attachment.Stream.CanRead.ShouldBeFalse();

        await uploadService.Received(1).UploadFile(
            Arg.Any<Stream>(), attachment.Length, "result.mp4", "video/mp4", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MaterializeAttachments_ShouldDropAndCountOversizedAttachment_WhenLargerThanBothLimits()
    {
        // Arrange
        var uploadService = Substitute.For<IUploadService>();
        uploadService.MaxFileSizeBytes.Returns(S3Limit);

        var handler = BuildHandler(maxAttachments: 5, uploadService);
        var attachment = Attachment("huge.mp4", S3Limit + 1);
        var response = new ChatAssistantResponse
        {
            Response = "done",
            Attachments = [attachment]
        };

        // Act
        var result = await handler.MaterializeAttachmentsAsync(response, 5, DiscordLimit, CancellationToken.None);
        AiCommandHandler.ApplyAttachmentNotes(response, result);

        // Assert
        result.OversizedCount.ShouldBe(1);
        result.OmittedCount.ShouldBe(0);

        attachment.DiscordAttachment.ShouldBeNull();
        attachment.S3AttachmentUrl.ShouldBeNull();
        attachment.Stream.CanRead.ShouldBeFalse();

        response.Response.ShouldBe($"done\n\n1 attachment(s) omitted (exceeds the {S3Limit} byte upload limit).");

        await uploadService.DidNotReceiveWithAnyArgs()
            .UploadFile(default!, default, default, default!, default);
    }

    [Test]
    public async Task MaterializeAttachments_ShouldDropAttachment_WhenNoS3ConfiguredAndAboveDiscordLimit()
    {
        // Arrange
        var handler = BuildHandler(maxAttachments: 5, uploadService: null);
        var attachment = Attachment("big.mp4", DiscordLimit + 1);
        var response = new ChatAssistantResponse { Attachments = [attachment] };

        // Act
        var result = await handler.MaterializeAttachmentsAsync(response, 5, DiscordLimit, CancellationToken.None);

        // Assert
        result.OversizedCount.ShouldBe(1);
        attachment.DiscordAttachment.ShouldBeNull();
        attachment.S3AttachmentUrl.ShouldBeNull();
        attachment.Stream.CanRead.ShouldBeFalse();
    }

    [Test]
    public async Task Invoke_ShouldDisposeSandboxOnce_WhenAssistantThrows()
    {
        // Arrange
        var sandbox = new RecordingSandbox();
        var handler = new TestableAiCommandHandler(
            sandbox,
            maxAttachments: 5,
            (_, _) => throw new InvalidOperationException("assistant exploded"));

        // Act
        var act = () => handler.Invoke(
            "hello", 1, "tester", 2, 3, DiscordLimit, new List<NetCord.Rest.RestMessage>());

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(act);
        sandbox.DisposeCount.ShouldBe(1);
    }

    [Test]
    public async Task Invoke_ShouldDisposeSandboxOnce_AndApplyAttachmentNotes_OnSuccess()
    {
        // Arrange
        var sandbox = new RecordingSandbox();
        var attachments = new[]
        {
            Attachment("a.txt", 10),
            Attachment("b.txt", 10),
            Attachment("c.txt", 10),
        };
        var handler = new TestableAiCommandHandler(
            sandbox,
            maxAttachments: 2,
            (_, _) => Task.FromResult(new ChatAssistantResponse
            {
                Response = "here you go",
                Attachments = [..attachments]
            }));

        // Act
        var response = await handler.Invoke(
            "hello", 1, "tester", 2, 3, DiscordLimit, new List<NetCord.Rest.RestMessage>());

        // Assert
        sandbox.DisposeCount.ShouldBe(1);
        response.Attachments.Count.ShouldBe(2);
        response.Response.ShouldBe("here you go\n\n1 attachment(s) omitted (limit is 2 per response).");
    }

    private sealed class RecordingSandbox : ISandbox
    {
        public int DisposeCount { get; private set; }

        public SandboxMetadata Metadata => throw new NotSupportedException();

        public ISandboxContainer? Container => null;

        public Task<SandboxMetadata> InitializeAsync(string sessionId, ulong callerId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<ISandboxContainer> EnsureStartedAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestableAiCommandHandler(
        ISandbox sandbox,
        int maxAttachments,
        Func<ChatAssistantContext, CancellationToken, Task<ChatAssistantResponse>> invoke)
        : AiCommandHandler(
            chatAssistant: null!,
            sandbox: sandbox,
            gateway: Gateway,
            interactionConfiguration: FixedSettings(maxAttachments),
            dbContext: null!,
            dateTimeProvider: null!)
    {
        internal override ulong? SelfUserId => SelfId;

        internal override Task<ChatAssistantResponse> InvokeAssistant(
            ChatAssistantContext context, CancellationToken ct)
            => invoke(context, ct);

        private static IInteractionConfiguration FixedSettings(int maxAttachments)
        {
            // The handler binds the scope itself from the arguments it was called with.
            return new StubInteractionConfiguration().With(BuildSettings(maxAttachments));
        }
    }
}
