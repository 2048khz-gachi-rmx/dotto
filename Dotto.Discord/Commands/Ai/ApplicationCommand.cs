using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Settings;
using Dotto.Common;
using Dotto.Common.DateTimeProvider;
using Dotto.Discord.CommandHandlers.Ai;
using Dotto.Discord.CommandHandlers.Download;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Commands.Ai;

public class ApplicationCommand(
    IServiceProvider serviceProvider,
    IDateTimeProvider dateTimeProvider,
    ILogger<ApplicationCommand> logger)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly AiCommandHandler _aiHandler = serviceProvider.GetRequiredService<AiCommandHandler>();
    private readonly AiSettings _settings = serviceProvider.GetRequiredService<IOptions<AiSettings>>().Value;

    private async Task Invoke(string userRequest)
    {
        // Only the followup needs the interaction acknowledged, so the round trip
        // overlaps with the history fetch and the assistant call.
        var acknowledge = RespondAsync(InteractionCallback.DeferredMessage());

        ChatAssistantResponse response;
        try
        {
            var (messageHistory, historyUnavailable) = await GetMessageHistoryAsync();

            var now = dateTimeProvider.UtcNow;

            messageHistory = messageHistory
                .Where(msg => (now - msg.CreatedAt) < _settings.ChatAssistant.HistoryMaxAge)
                .ToList();

            var uploadLimit = DownloadCommandHandler.GetMaxDiscordFileSize(Context.Guild, Context.User);

            List<ContextNote> notes = [];
            if (historyUnavailable is not null)
                notes.Add(new("Message history", historyUnavailable));

            response = await _aiHandler.Invoke(userRequest,
                Context.User.Id,
                Context.User.Username,
                Context.Channel.Id,
                Context.Guild?.Id ?? 0,
                uploadLimit,
                messageHistory,
                notes);

            await acknowledge;
        }
        catch
        {
            // The error handler answers with a response of its own, which needs the
            // interaction acknowledged; report the original failure, not the ack.
            try
            {
                await acknowledge;
            }
            catch (Exception)
            {
                // The original exception is the one that gets reported.
            }

            throw;
        }

        // Workaround: interactions can't expose options to history, so embed the prompt.
        var textResponse = $"-# prompt: {userRequest.Replace("\n", "")}\n"
                           + response.Response;
        var s3Atts = response.Attachments
            .Select(att => att.S3AttachmentUrl)
            .Where(att => att != null)
            .ToList();

        if (!s3Atts.IsNullOrEmpty())
            textResponse += "\n" + string.Join("\n", s3Atts.Select(url => "-# " + url));

        var sentMessage = await FollowupAsync(new()
        {
            Content = textResponse,
            Attachments = response.Attachments.Select(att => att.DiscordAttachment).Where(att => att != null)!
        });

        await _aiHandler.LogUploadedMedia(sentMessage, response, Context.User.Id);
    }

    /// <summary>
    /// Recent channel messages, oldest first, plus why they are missing when they are.
    /// Best effort: history is context, not a requirement, so a failed fetch only
    /// becomes a note for the model.
    /// </summary>
    private async Task<(List<RestMessage> Messages, string? UnavailableReason)> GetMessageHistoryAsync()
    {
        // A DM between users (or a group DM) is a channel the bot is not a member of;
        // Discord answers 403 (50001 Missing Access) for its history. DMs with the bot
        // itself are readable, so only this context is skipped.
        if (Context.Interaction.Context == InteractionContextType.DMChannel)
            return ([], "This conversation is a DM the bot is not a member of; it cannot read " +
                        "that channel's history, so no prior messages are included.");

        try
        {
            return (await Context.Channel.GetMessagesAsync(new()
                {
                    BatchSize = Math.Min(_settings.ChatAssistant.HistoryContextSize, 100),
                    Direction = PaginationDirection.Before,
                    From = Context.Interaction.Id
                })
                .Take(_settings.ChatAssistant.HistoryContextSize)
                .OrderBy(msg => msg.CreatedAt)
                .ToListAsync(), null);
        }
        catch (Exception ex) when (ex is RestException or HttpRequestException)
        {
            logger.LogWarning(ex,
                "Could not fetch message history for channel {ChannelId}; continuing without it",
                Context.Channel.Id);

            return ([], "Message history could not be fetched for this channel, so no prior " +
                        "messages are included.");
        }
    }

    [SlashCommand("ai", "Chat with the AI assistant",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel],
        DefaultGuildPermissions = Permissions.SendMessages)]
    public Task InvokeSlash(
        [SlashCommandParameter(Name = "prompt", Description = "What you want to ask the assistant")]
        string userRequest)
        => Invoke(userRequest);

    [MessageCommand("Ask AI",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel])]
    public async Task InvokeMessage(RestMessage message)
        => await Invoke(message.Content);
}