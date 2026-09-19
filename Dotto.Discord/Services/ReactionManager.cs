using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Dotto.Common.DateTimeProvider;
using NetCord.Rest;

namespace Dotto.Discord.Services;

/// <summary>
/// Distinguishes what a tracked reaction session is for, so multiple reaction-driven
/// workflows can coexist without consuming each other's sessions.
/// </summary>
public enum ReactionSessionKind
{
    Compression,
    Download
}

public record ReactionSession(
    object Payload,
    ulong MessageId,
    ulong ChannelId,
    DateTime ExpiresAt,
    ReactionSessionKind Kind);

public class ReactionManager(IDateTimeProvider dateTimeProvider)
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<ulong, ReactionSession> _sessions = new();

    public void TrackMessage(RestMessage message, object payload, ReactionSessionKind kind)
        => TrackMessage(message.Id, message.ChannelId, payload, kind);

    public void TrackMessage(ulong messageId, ulong channelId, object payload, ReactionSessionKind kind)
    {
        var session = new ReactionSession(
            payload,
            messageId,
            channelId,
            dateTimeProvider.UtcNow.Add(SessionTtl),
            kind);

        _sessions.TryAdd(messageId, session);
    }

    public bool TryGetSession(ulong messageId, ReactionSessionKind kind, [NotNullWhen(true)] out ReactionSession? session)
    {
        if (!_sessions.TryGetValue(messageId, out session))
            return false;

        // another workflow owns this session; leave it alone
        if (session!.Kind != kind)
        {
            session = null;
            return false;
        }

        if (dateTimeProvider.UtcNow > session.ExpiresAt)
        {
            _sessions.TryRemove(messageId, out _);
            session = null;
            return false;
        }

        return true;
    }

    public bool RemoveSession(ulong messageId)
        => _sessions.TryRemove(messageId, out _);

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
