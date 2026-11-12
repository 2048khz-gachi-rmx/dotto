using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Settings;
using Dotto.Application.Configuration;
using Dotto.Common;
using Dotto.Common.DateTimeProvider;
using Dotto.Discord.CommandHandlers.Ai;
using Dotto.Discord.CommandHandlers.Download;
using Dotto.Discord.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace Dotto.Discord.Commands.Ai;

public class TextCommand(
    IServiceProvider serviceProvider,
    IDateTimeProvider dateTimeProvider,
    IInteractionConfiguration interactionConfiguration,
    RestClient client,
    ILogger<TextCommand> logger) : CommandModule<CommandContext>
{
    private readonly AiCommandHandler _aiHandler = serviceProvider.GetRequiredService<AiCommandHandler>();

    [Command("ai")]
    public async Task Invoke([CommandParameter(Remainder = true)] string userRequest)
    {
        if (Context.Message.GuildId == null)
            return;
        
        var channelId = Context.Message.ChannelId;
        var guildId = Context.Message.GuildId.Value;

        interactionConfiguration.SetScope(new InteractionScope(guildId, channelId, Context.User.Id));

        var settings = await interactionConfiguration.GetCurrentAsync<AiSettings>();

        // Skip the invoking message so the query isn't duplicated in history.
        var messageHistory = await client.GetMessagesAsync(channelId, new()
            {
                BatchSize = Math.Min(settings.ChatAssistant.HistoryContextSize, 100),
                Direction = PaginationDirection.Before,
                From = Context.Message.Id
            })
            .Take(settings.ChatAssistant.HistoryContextSize)
            .Where(msg => msg.Id != Context.Message.Id)
            .OrderBy(msg => msg.CreatedAt)
            .ToListAsync();

        var now = dateTimeProvider.UtcNow;

        messageHistory = messageHistory
            .Where(msg => (now - msg.CreatedAt) < settings.ChatAssistant.HistoryMaxAge)
            .ToList();

        var uploadLimit = DownloadCommandHandler.GetMaxDiscordFileSize(Context.Guild, Context.User);

        var progress = new AgentProgress(settings.ChatAssistant.MaxTurns);

        // One placeholder message carries the activity and is later replaced by the answer.
        var placeholder = await ReplyAsync(new ReplyMessageProperties { Content = "-# thinking…" });

        ChatAssistantResponse response;
        try
        {
            await using (var activity = AgentActivityMessage.Create(
                             progress,
                             options => placeholder.ModifyAsync(options),
                             logger))
            {
                response = await _aiHandler.Invoke(userRequest,
                    Context.User.Id,
                    Context.User.Username,
                    channelId,
                    guildId,
                    uploadLimit,
                    messageHistory,
                    progress: progress);
            }
        }
        catch
        {
            // The error handler posts its own message; don't leave a stale placeholder.
            try
            {
                await placeholder.DeleteAsync();
            }
            catch (Exception)
            {
                // The original exception is the one that gets reported.
            }

            throw;
        }

        var s3Atts = response.Attachments
            .Select(att => att.S3AttachmentUrl)
            .Where(att => att != null)
            .Select(att => att!.ToString())
            .ToList();

        var composed = AgentRunMessage.Compose(
            response.Response,
            progress,
            verbose: false,
            settings.ChatAssistant.TranscriptTurns,
            response.Duration,
            response.Attachments.Count,
            links: s3Atts);

        var attachments = response.Attachments
            .Select(att => att.DiscordAttachment)
            .Where(att => att != null)
            .Select(att => att!)
            .ToList();

        // The answer outgrew the message limit; the full text rides along as a file.
        if (composed.HasOverflow)
        {
            if (attachments.Count >= AgentRunMessage.MaxDiscordAttachments)
            {
                // The answer text wins the last slot: media still lives in the sandbox/S3.
                logger.LogWarning(
                    "Dropping the last attachment so the full answer can be attached as {File}",
                    AgentRunMessage.OverflowFileName);
                attachments.RemoveAt(attachments.Count - 1);
            }

            attachments.Add(AgentRunMessage.CreateOverflowAttachment(composed));
        }

        RestMessage sentMessage;
        try
        {
            sentMessage = await placeholder.ModifyAsync(options =>
            {
                options.Content = composed.Content;
                options.Attachments = attachments;
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not replace the ai activity message; replying with the answer instead");

            sentMessage = await ReplyAsync(new ReplyMessageProperties
            {
                Content = composed.Content,
                Attachments = attachments,
            });
        }

        await _aiHandler.LogUploadedMedia(sentMessage, response, Context.User.Id);
    }
}
