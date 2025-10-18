using Dotto.Common;
using Dotto.Discord.CommandHandlers.Download;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace Dotto.Discord.Commands.Download;

public class TextCommand(DownloadCommand dl,
    RestClient client) : CommandModule<CommandContext>
{
    [Command("dl")]
    public async Task InvokeMessage(string uriString, bool audioOnly = false)
    {
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            await ReplyAsync(new() { Content = "No URL matched" });
            return;
        }

        // capture the typing state resource
        var typingTask = client.EnterTypingStateAsync(Context.Message.ChannelId);
        
        try
        {
            var uploadLimit = DownloadCommand.GetMaxDiscordFileSize(Context.Guild);
            var msg = await dl.CreateMessage<ReplyMessageProperties>(uri, audioOnly, uploadLimit);
            var replyTask = ReplyAsync(msg.Message);
            
            await Context.Message.SuppressEmbeds();
            var newMessage = await replyTask;
            await dl.LogDownloadedMedia(newMessage, msg, Context.User, uri);
        }
        finally
        {
            _ = typingTask.ContinueWith(task => task.Result.Dispose());
        }
    }
}