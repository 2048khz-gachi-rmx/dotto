using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Dotto.Application.InternalServices;
using Dotto.Common;
using Dotto.Common.Constants;
using Dotto.Discord.EventHandlers;
using Dotto.Discord.Services;
using Dotto.Discord.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.Commands.Download;

/// <summary>
/// Handles URLs posted in chat when the channel has the <see cref="Constants.ChannelFlags.FunctionalFlags.LinkAutodownload"/> flag.
/// <para>
/// Regular URLs (<see cref="AutoDownloadSettings.Patterns"/>) are downloaded immediately and the original's embeds suppressed.
/// Ambiguous URLs (<see cref="AutoDownloadSettings.AmbiguousPatterns"/>, e.g. x.com) instead get an inbox-tray reaction from
/// the bot and are only downloaded once a human confirms with the same reaction — this keeps the original embed as context.
/// </para>
/// </summary>
public class MessageUrlDownload(
    IOptionsMonitor<AutoDownloadSettings> settings,
    IChannelFlagsService channelFlagsService,
    IServiceProvider serviceProvider,
    ReactionManager reactionManager,
    ILogger<MessageUrlDownload> logger)
    : IGatewayEventProcessor<Message>, IDisposable
{
    /// <summary>The reaction the bot adds to ambiguous URLs, and expects back to confirm a download.</summary>
    internal static readonly ReactionEmojiProperties DownloadReaction = new("📥");

    private readonly MessageDownloadExecutor _executor = serviceProvider.GetRequiredService<MessageDownloadExecutor>();

    // precompile all regexes
    private static Regex[]? _patterns;
    private static Regex[]? _ambiguousPatterns;
    private readonly IDisposable? _changeTracker = settings.OnChange(GenerateRegexes);

    [MemberNotNull(nameof(_patterns), nameof(_ambiguousPatterns))]
    private static void GenerateRegexes(AutoDownloadSettings settings)
    {
        _patterns = settings.Patterns.Select(str => new Regex(str, RegexOptions.Compiled)).ToArray();
        _ambiguousPatterns = settings.AmbiguousPatterns.Select(str => new Regex(str, RegexOptions.Compiled)).ToArray();
    }

    public async ValueTask HandleAsync(Message message)
    {
        if (_patterns == null || _ambiguousPatterns == null)
            GenerateRegexes(settings.CurrentValue);

        if (message.Author.IsBot)
            return;

        var flags = await channelFlagsService.GetChannelFlags(message.ChannelId);
        if (!flags.Contains(Constants.ChannelFlags.FunctionalFlags.LinkAutodownload))
            return;

        // I won't bother with supporting multiple URLs in a message since i believe noone ever posts multiple,
        // but let's log them in case i'm wrong
        if (CountMatches(message.Content) > 1)
        {
            logger.LogWarning("Someone posted more than 1 downloadable URLs in chat just to spite me");
        }

        // ambiguous URLs take precedence so an x.com link isn't downloaded before someone opts in
        var ambiguous = FirstMatch(_ambiguousPatterns, message.Content);
        if (ambiguous != null)
        {
            await RequestDownloadReaction(message, ambiguous);
            return;
        }

        var regular = FirstMatch(_patterns, message.Content);
        if (regular == null)
            return;

        await _executor.DownloadAndReplyAsync(message, regular, suppressEmbeds: true);
    }

    private async Task RequestDownloadReaction(Message message, Uri uri)
    {
        await message.AddReactionAsync(DownloadReaction);
        reactionManager.TrackMessage(message.Id, message.ChannelId, new PendingDownload(message, uri), ReactionSessionKind.Download);
    }

    private int CountMatches(string text)
        => _ambiguousPatterns!.Sum(p => p.Matches(text).Count)
           + _patterns!.Sum(p => p.Matches(text).Count);

    private static Uri? FirstMatch(Regex[] patterns, string text)
    {
        foreach (var pattern in patterns)
        {
            var match = pattern.Match(text);
            if (match.Success)
                return new Uri(match.Value);
        }

        return null;
    }

    public void Dispose()
    {
        _changeTracker?.Dispose();
    }
}

/// <summary>A message whose media is waiting for a human's confirmation reaction before it gets downloaded.</summary>
public record PendingDownload(Message SourceMessage, Uri Url);
