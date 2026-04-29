using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Dotto.Common.DateTimeProvider;

namespace Dotto.Discord.Services;

public record ReactionSession(
    ulong OriginalMessageId,
    ulong BotReplyMessageId,
    ulong OriginalAuthorId,
    ulong ChannelId,
    DateTime ExpiresAt);

public class ReactionManager(IDateTimeProvider dateTimeProvider)
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<ulong, ReactionSession> _sessions = new();

    public void TrackMessage(ulong originalMessageId, ulong botReplyMessageId, ulong originalAuthorId, ulong channelId)
    {
        var session = new ReactionSession(
            originalMessageId,
            botReplyMessageId,
            originalAuthorId,
            channelId,
            dateTimeProvider.UtcNow.Add(SessionTtl));

        _sessions.TryAdd(botReplyMessageId, session);
    }

    public bool TryGetSession(ulong botReplyMessageId, [NotNullWhen(true)] out ReactionSession? session)
    {
        if (!_sessions.TryGetValue(botReplyMessageId, out session))
            return false;

        if (dateTimeProvider.UtcNow > session!.ExpiresAt)
        {
            _sessions.TryRemove(botReplyMessageId, out _);
            return false;
        }

        return true;
    }

    public bool RemoveSession(ulong botReplyMessageId)
        => _sessions.TryRemove(botReplyMessageId, out _);

    public void CleanupExpired()
    {
        var now = dateTimeProvider.UtcNow;
        foreach (var kvp in _sessions)
        {
            if (now > kvp.Value.ExpiresAt)
                _sessions.TryRemove(kvp.Key, out _);
        }
    }
}
