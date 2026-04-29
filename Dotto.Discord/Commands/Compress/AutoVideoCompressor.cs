using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Dotto.Application.InternalServices;
using Dotto.Common;
using Dotto.Common.Constants;
using Dotto.Discord.CommandHandlers.Compress;
using Dotto.Discord.EventHandlers;
using Dotto.Discord.Services;
using Dotto.Ffmpeg.Contracts;
using Dotto.Ffmpeg.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.Commands.Compress;

public class AutoVideoCompressor(
    RestClient client,
    IChannelFlagsService channelFlagsService,
    ReactionManager reactionManager,
    IOptions<CompressionSettings> compressionSettings,
    IServiceProvider serviceProvider)
    : IGatewayEventProcessor<Message>, IDisposable
{
    private readonly CompressCommandHandler _compressHandler = serviceProvider.GetRequiredService<CompressCommandHandler>();
    private readonly CompressionMethod _defaultMethod = compressionSettings.Value.DefaultStrategy;

    private static readonly ReactionEmojiProperties _thumbsUp = new("👍");
    private static readonly ReactionEmojiProperties _middleFinger = new("🖕");
    private static readonly ReactionEmojiProperties _ouse = new("ouse", 1164630871589003326);

    public async ValueTask HandleAsync(Message message)
    {
        if (message.Author.IsBot)
            return;
        
        var flags = await channelFlagsService.GetChannelFlags(message.ChannelId);
        if (!flags.Contains(Constants.ChannelFlags.FunctionalFlags.VideoRecompress))
            return;
        
        var videosToProcess = new List<(Uri Url, string Name)>();
        var contentToUse = message.Content;

        foreach (var attachment in message.Attachments)
        {
            var contentType = attachment.ContentType;
            if (contentType == null || !contentType.StartsWith("video/"))
                continue;

            videosToProcess.Add((new Uri(attachment.Url), attachment.Title ?? attachment.FileName));
        }

        if (!string.IsNullOrEmpty(message.Content))
        {
            foreach (Match match in Constants.Compression.Regexes.DiscordCdn.Matches(message.Content))
            {
                var url = match.Value;
                var fn = match.Groups[2].Value.ToLower();
                
                if (!Constants.Compression.Regexes.VideoExts.IsMatch(fn))
                    continue;

                videosToProcess.Add((new Uri(url), fn));
            }

            contentToUse = Constants.Compression.Regexes.DiscordCdn.Replace(message.Content, "");
        }

        if (videosToProcess.IsEmpty())
            return;

        var typingTask = client.EnterTypingStateAsync(message.ChannelId);

        try
        {
            var result = await _compressHandler.CreateMessage<ReplyMessageProperties>(videosToProcess, _defaultMethod, true);

            if (result.HasAnyMedia)
            {   
                var byAuthorString = $"-# by <@{message.Author.Id}>";

                // yes this is kinda ugly
                var newContent = byAuthorString + " " + result.Message.Content!.ReplacePrefix("-# ")
                                 + "\n" + contentToUse;

                var newMessage = result.Message.WithContent(newContent)
                    .WithAllowedMentions(AllowedMentionsProperties.None);

                var replyTask = message.ReplyAsync(newMessage);
                var reply = await replyTask;

                var removeEmoji = Random.Shared.NextDouble() < 0.01 ? _ouse : _middleFinger;
                await reply.AddReactionAsync(_thumbsUp);
                await reply.AddReactionAsync(removeEmoji);

                reactionManager.TrackMessage(message.Id, reply.Id, message.Author.Id, message.ChannelId);
            }
        }
        catch
        {
            // Reaction tracking is best-effort
        }
        finally
        {
            _ = typingTask.ContinueWith(task => task.Result.Dispose());
        }
    }

    public void Dispose()
    {
    }
}
