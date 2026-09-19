using Dotto.Common;
using Dotto.Discord.CommandHandlers.Download;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.Services;

/// <summary>
/// Downloads the media behind a URL and replies to the source message with it.
/// Shared by the immediate auto-download flow and the reaction-confirmed (ambiguous URL) flow.
/// </summary>
internal class MessageDownloadExecutor(DownloadCommandHandler downloadHandler, RestClient client)
{
    public async Task DownloadAndReplyAsync(Message message, Uri uri, bool suppressEmbeds)
    {
        var typingTask = client.EnterTypingStateAsync(message.ChannelId);

        try
        {
            var uploadLimit = DownloadCommandHandler.GetMaxDiscordFileSize(message.Guild);
            var msg = await downloadHandler.CreateMessage<ReplyMessageProperties>(uri, false, uploadLimit);

            if (!msg.HasAnyMedia)
                return;

            var replyTask = message.ReplyAsync(msg.Message);

            if (suppressEmbeds)
                await message.SuppressEmbeds();

            var newMessage = await replyTask;
            await downloadHandler.LogDownloadedMedia(newMessage, msg, message.Author.Id);
        }
        finally
        {
            _ = typingTask.ContinueWith(task => task.Result.Dispose());
        }
    }
}
