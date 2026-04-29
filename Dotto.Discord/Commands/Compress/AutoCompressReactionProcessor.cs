using Dotto.Discord.EventHandlers;
using Dotto.Discord.Services;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.Commands.Compress;

internal class AutoCompressReactionProcessor(
    ReactionManager reactionManager,
    GatewayClient gatewayClient)
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

        if (await IsAuthorized(user, session) == false)
            return;

        reactionManager.RemoveSession(args.MessageId);

        await ExecuteAction(args, session);
    }

    private async Task<bool> IsAuthorized(User user, ReactionSession session)
    {
        var sourceMessage = session.Payload as Message;
        if (sourceMessage?.GuildId == null)
            return false;
        
        if (user.Id == sourceMessage.Author.Id)
            return true;

        // two roundtrips eeewwwwwwww
        var restGuild = await gatewayClient.Rest.GetGuildAsync(sourceMessage.GuildId.Value);
        var guildUser = await gatewayClient.Rest.GetGuildUserAsync(sourceMessage.GuildId.Value, sourceMessage.Author.Id);
        
        var perms = guildUser.GetPermissions(restGuild);
        
        return (perms & Permissions.Administrator) != 0;
    }

    private async Task ExecuteAction(MessageReactionAddEventArgs args, ReactionSession session)
    {
        var sourceMessage = session.Payload as Message;
        if (sourceMessage == null)
            return;
        
        try
        {
            if (IsRemove(args.Emoji))
            {
                await gatewayClient.Rest.DeleteMessageAsync(session.ChannelId, session.BotReplyMessageId);
                return;
            }
            
            if (IsThumbsUp(args.Emoji))
            {
                try
                {
                    await gatewayClient.Rest.DeleteMessageAsync(sourceMessage.ChannelId, sourceMessage.Id);
                } catch { /* source message deleted, whatever. remove the emojis */ }

                foreach (var reactionToRemove in AcceptEmojis.Concat(RejectEmojis))
                {
                    try
                    {
                        await gatewayClient.Rest.DeleteAllMessageReactionsForEmojiAsync(session.ChannelId, session.BotReplyMessageId, reactionToRemove);
                    } catch { /* don't care didn't ask */ }
                }
            }
        }
        catch { /* message may already be deleted */ }
    }

    private bool IsRemove(MessageReactionEmoji emote)
        => RejectEmojis.Any(f => f.Name == emote.Name && f.Id == emote.Id);

    private bool IsThumbsUp(MessageReactionEmoji emote)
        => emote.Name == "👍";
}
