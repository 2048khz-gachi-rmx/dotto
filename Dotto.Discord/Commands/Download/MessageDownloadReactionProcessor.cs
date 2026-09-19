using Dotto.Discord.EventHandlers;
using Dotto.Discord.Services;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.Commands.Download;

/// <summary>
/// Confirms a pending ambiguous-URL download when a human reacts with the inbox-tray reaction
/// the bot added in <see cref="MessageUrlDownload"/>. The original embeds are left intact.
/// </summary>
internal class MessageDownloadReactionProcessor(
    ReactionManager reactionManager,
    MessageDownloadExecutor executor,
    RestClient client,
    ILogger<MessageDownloadReactionProcessor> logger)
    : IGatewayEventProcessor<MessageReactionAddEventArgs>
{
    public async ValueTask HandleAsync(MessageReactionAddEventArgs args)
    {
        if (args.User == null || args.User.IsBot)
            return;

        if (!IsDownloadReaction(args.Emoji))
            return;

        if (!reactionManager.TryGetSession(args.MessageId, ReactionSessionKind.Download, out var session))
            return;

        if (session.Payload is not PendingDownload pending)
            return;

        // consume the session so a download can only be triggered once
        reactionManager.RemoveSession(args.MessageId);

        // remove the bot's own reaction to signal it has been handled
        try
        {
            await client.DeleteCurrentUserMessageReactionAsync(pending.SourceMessage.ChannelId, args.MessageId, MessageUrlDownload.DownloadReaction);
        }
        catch { /* message may already be gone */ }

        try
        {
            await executor.DownloadAndReplyAsync(pending.SourceMessage, pending.Url, suppressEmbeds: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to download confirmed ambiguous URL {Url} from message {MessageId}", pending.Url, args.MessageId);
        }
    }

    private static bool IsDownloadReaction(MessageReactionEmoji emoji)
        => emoji.Name == MessageUrlDownload.DownloadReaction.Name;
}
