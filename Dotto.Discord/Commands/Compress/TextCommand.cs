using Dotto.Discord.CommandHandlers.Compress;
using Dotto.Ffmpeg.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace Dotto.Discord.Commands.Compress;

public class TextCommand(IServiceProvider serviceProvider, RestClient client) : CommandModule<CommandContext>
{
    private readonly CompressCommandHandler _compressHandler = serviceProvider.GetRequiredService<CompressCommandHandler>();

    [Command("compress")]
    public async Task Invoke(string? format = "vp9")
    {
        var videos = Context.Message.Attachments
            .Where(a => a.ContentType?.StartsWith("video/") == true)
            .Select(a => (new Uri(a.Url), a.FileName))
            .ToList();

        if (videos.Count == 0)
        {
            await ReplyAsync(new() { Content = "No video attachments found" });
            return;
        }

        var method = format == "av1" ? CompressionMethod.Av1 : CompressionMethod.Vp9;
        var typingTask = client.EnterTypingStateAsync(Context.Message.ChannelId);

        try
        {
            var msg = await _compressHandler.CreateMessage<ReplyMessageProperties>(videos, method, false);
            var replyTask = ReplyAsync(msg.Message);

            var newMessage = await replyTask;
        }
        finally
        {
            _ = typingTask.ContinueWith(task => task.Result.Dispose());
        }
    }
}
