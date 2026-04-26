using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Dotto.Application.InternalServices;
using Dotto.Common;
using Dotto.Common.Constants;
using Dotto.Discord.CommandHandlers.Compress;
using Dotto.Discord.EventHandlers;
using Dotto.Ffmpeg.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.Commands.Compress;

public class AutoVideoCompressor(
    RestClient client,
    IChannelFlagsService channelFlagsService,
    IServiceProvider serviceProvider)
    : IGatewayEventProcessor<Message>, IDisposable
{
    private readonly CompressCommandHandler _compressHandler = serviceProvider.GetRequiredService<CompressCommandHandler>();
    
    private static Regex? _discordCdnRegex;
    private static Regex? _videoExtsRegex;

    [MemberNotNull(nameof(_discordCdnRegex), nameof(_videoExtsRegex))]
    private void InitializeRegexes()
    {
        _discordCdnRegex = new Regex(
            @"https?:\/\/(?:media|cdn)\.discord(?:app)?\.(?:net|com)\/attachments\/(\d{18,}\/\d{18,})\/(.*\.\w{3,}).*$",
            RegexOptions.Compiled);
        
        _videoExtsRegex = new Regex(@"\.(mov|mp4|webm)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public async ValueTask HandleAsync(Message message)
    {
        if (_discordCdnRegex == null || _videoExtsRegex == null)
            InitializeRegexes();

        if (message.Author.IsBot)
            return;
        
        var flags = await channelFlagsService.GetChannelFlags(message.ChannelId);
        if (!flags.Contains(Constants.ChannelFlags.FunctionalFlags.VideoRecompress))
            return;
        
        var videosToProcess = new List<(Uri Url, string Name)>();

        foreach (var attachment in message.Attachments)
        {
            var contentType = attachment.ContentType;
            if (contentType == null || !contentType.StartsWith("video/"))
                continue;

            videosToProcess.Add((new Uri(attachment.Url), attachment.Title ?? attachment.FileName));
        }

        if (!string.IsNullOrEmpty(message.Content))
        {
            foreach (Match match in _discordCdnRegex.Matches(message.Content))
            {
                var url = match.Value;
                var fn = match.Groups[2].Value.ToLower();
                
                if (!_videoExtsRegex.IsMatch(fn))
                    continue;

                videosToProcess.Add((new Uri(url), fn));
            }
        }

        if (videosToProcess.IsEmpty())
            return;

        await CompressFromMessage(message, videosToProcess);
    }

    private async ValueTask CompressFromMessage(RestMessage message, List<(Uri Url, string Name)> videos)
    {
        var typingTask = client.EnterTypingStateAsync(message.ChannelId);

        try
        {
            var result = await _compressHandler.CreateMessage<ReplyMessageProperties>(videos, CompressionMethod.Vp9, true);

            if (!result.HasAnyMedia)
                return;
            
            var replyTask = message.ReplyAsync(result.Message);
            await replyTask;
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
