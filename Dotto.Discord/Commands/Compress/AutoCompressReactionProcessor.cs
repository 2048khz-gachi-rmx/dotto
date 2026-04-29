using Dotto.Discord.EventHandlers;
using Dotto.Discord.Services;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.Commands.Compress;

internal class AutoCompressReactionProcessor(
    ReactionManager reactionManager,
    RestClient client)
    : IGatewayEventProcessor<MessageReactionAddEventArgs>
{
    // emojis that we interpret as "delete original message"
    private static readonly ReactionEmojiProperties[] AcceptEmojis =
    [
        new("👍")
    ];
    
    // reactions that we interpret as "delete this message"
    private static readonly ReactionEmojiProperties[] RejectEmojis =
    [
        new("🖕"),
        new("ouse", 1164630871589003326) // TODO: unhardcode the ouse emoji (config)
    ];
    
    public async ValueTask HandleAsync(MessageReactionAddEventArgs args)
    {
        if (args.User is not User user || user.IsBot)
            return;

        if (!reactionManager.TryGetSession(args.MessageId, out var session))
            return;

        if (!IsAuthorized(user, session))
            return;

        reactionManager.RemoveSession(args.MessageId);

        await ExecuteAction(args, session);
    }

    private bool IsAuthorized(User user, ReactionSession session)
    {
        if (user.Id == session.OriginalAuthorId)
            return true;

        // Admin check would require guild member lookup;
        // for now only the original author can trigger actions
        return false;
    }

    private async Task ExecuteAction(MessageReactionAddEventArgs args, ReactionSession session)
    {
        try
        {
            if (IsThumbsUp(args.Emoji))
            {
                await client.DeleteMessageAsync(session.ChannelId, session.OriginalMessageId);

                foreach (var reactionToRemove in AcceptEmojis.Concat(RejectEmojis))
                {
                    try
                    {
                        await client.DeleteAllMessageReactionsForEmojiAsync(session.ChannelId, session.BotReplyMessageId, reactionToRemove);
                    } catch { /* don't care didn't ask */ }
                }
            }
            else if (IsRemove(args.Emoji))
            {
                await client.DeleteMessageAsync(session.ChannelId, session.BotReplyMessageId);
            }
        }
        catch { /* message may already be deleted */ }
    }

    private bool IsRemove(MessageReactionEmoji emote)
        => RejectEmojis.Any(f => f.Name == emote.Name && f.Id == emote.Id);

    private bool IsThumbsUp(MessageReactionEmoji emote)
        => emote.Name == "👍";
}
