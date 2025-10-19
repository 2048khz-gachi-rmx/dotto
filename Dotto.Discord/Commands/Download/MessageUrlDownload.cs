using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Dotto.Application.InternalServices.ChannelFlagsService;
using Dotto.Common;
using Dotto.Common.Constants;
using Dotto.Discord.CommandHandlers.Download;
using Dotto.Discord.EventHandlers;
using Dotto.Discord.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.Commands.Download;

/// <summary>
/// Downloads media from messages containing an eligible URL is posted in chat, assuming the appropriate flag is set in the channel.
/// <seealso cref="Constants.ChannelFlags.FunctionalFlags.LinkAutodownload" />
/// </summary>
public class MessageUrlDownload(
    IOptionsMonitor<AutoDownloadSettings> settings,
    RestClient client,
    ChannelFlagsService channelFlagsService,
    IServiceProvider serviceProvider,
    ILogger<MessageUrlDownload> logger)
    : IGatewayEventProcessor<Message>, IDisposable
{
    private readonly DownloadCommandHandler _downloadHandler = serviceProvider.GetRequiredService<DownloadCommandHandler>();
    
    // precompile all regexes
    private static Regex[]? _patterns;
    private readonly IDisposable? _changeTracker = settings.OnChange(GenerateRegexes);

    [MemberNotNull(nameof(_patterns))]
    private static void GenerateRegexes(AutoDownloadSettings settings)
    {
        _patterns = settings.Patterns.Select(str => new Regex(str, RegexOptions.Compiled)).ToArray();
    }
    
    public async ValueTask HandleAsync(Message message)
    {
        if (_patterns == null)
            GenerateRegexes(settings.CurrentValue);

        if (message.Author.IsBot)
            return;
        
        var flags = await channelFlagsService.GetChannelFlags(message.ChannelId);
        if (!flags.Contains(Constants.ChannelFlags.FunctionalFlags.LinkAutodownload))
            return;
        
        var text = message.Content;
        var matchedUrls = new List<string>();
        
        foreach (var pattern in _patterns)
        {
            var match = pattern.Matches(text);
            matchedUrls.AddRange(match.Select(m => m.Value));
        }

        if (matchedUrls.IsEmpty())
            return;
        
        await DownloadFromMessage(message, matchedUrls);
    }

    private async ValueTask DownloadFromMessage(Message message, List<string> matchedUrls)
    {
        // I won't bother with supporting multiple URLs in a message since i believe noone ever posts multiple,
        // but let's log them in case i'm wrong
        if (matchedUrls.Count > 1)
        {
            logger.LogWarning("Someone posted more than 1 downloadable URLs in chat just to spite me");
        }
        
        var uri = new Uri(matchedUrls.First());
        
        var typingTask = client.EnterTypingStateAsync(message.ChannelId);

        try
        {
            var uploadLimit = DownloadCommandHandler.GetMaxDiscordFileSize(message.Guild);
            var msg = await _downloadHandler.CreateMessage<ReplyMessageProperties>(uri, false, uploadLimit);

            if (!msg.HasAnyMedia)
                return;
            
            var replyTask = message.ReplyAsync(msg.Message);

            await message.SuppressEmbeds();
            var newMessage = await replyTask;
            await _downloadHandler.LogDownloadedMedia(newMessage, msg, message.Author.Id);
        }
        finally
        {
            _ = typingTask.ContinueWith(task => task.Result.Dispose());
        }
    }
    
    public void Dispose()
    {
        _changeTracker?.Dispose();
    }
}