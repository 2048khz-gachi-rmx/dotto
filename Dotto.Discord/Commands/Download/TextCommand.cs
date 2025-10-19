using Dotto.Common;
using Dotto.Discord.CommandHandlers.Download;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace Dotto.Discord.Commands.Download;

public class TextCommand(IServiceProvider serviceProvider, RestClient client) : CommandModule<CommandContext>
{
    private readonly DownloadCommandHandler _downloadHandler = serviceProvider.GetRequiredService<DownloadCommandHandler>();
    
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
            var uploadLimit = DownloadCommandHandler.GetMaxDiscordFileSize(Context.Guild);
            var msg = await _downloadHandler.CreateMessage<ReplyMessageProperties>(uri, audioOnly, uploadLimit);
            var replyTask = ReplyAsync(msg.Message);
            
            await Context.Message.SuppressEmbeds();
            var newMessage = await replyTask;
            await _downloadHandler.LogDownloadedMedia(newMessage, msg, Context.User.Id);
        }
        finally
        {
            _ = typingTask.ContinueWith(task => task.Result.Dispose());
        }
    }
}