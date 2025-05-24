using Dotto.Application.Modules.Download;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace Dotto.Discord.Commands.Download;

public class TextCommand(DownloadCommand dl,
    RestClient client) : CommandModule<CommandContext>
{
    [Command("dl")]
    public async Task InvokeMessage(string uriString)
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
            var msg = await dl.CreateMessage<ReplyMessageProperties>(uri);
            await ReplyAsync(msg);
        }
        finally
        {
            _ = typingTask.ContinueWith(task => task.Result.Dispose());
        }
    }
}