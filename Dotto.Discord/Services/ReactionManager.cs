using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Dotto.Common.DateTimeProvider;
using NetCord.Rest;

namespace Dotto.Discord.Services;

public record ReactionSession(
    object Payload,
    ulong BotReplyMessageId,
    ulong ChannelId,
    DateTime ExpiresAt);

public class ReactionManager(IDateTimeProvider dateTimeProvider)
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<ulong, ReactionSession> _sessions = new();

    public void TrackMessage(RestMessage botMessage, object payload)
    {
        var session = new ReactionSession(
            payload,
            botMessage.Id,
            botMessage.ChannelId,
            dateTimeProvider.UtcNow.Add(SessionTtl));

        _sessions.TryAdd(botMessage.Id, session);
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
