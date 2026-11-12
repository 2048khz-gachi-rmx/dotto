using Microsoft.Extensions.Configuration;

namespace Dotto.Application.Configuration;

/// <summary>
/// The guild, channel, and user an interaction happened in. Guild is null for DMs;
/// zero IDs are treated as absent.
/// </summary>
public readonly record struct InteractionScope(ulong? GuildId, ulong ChannelId, ulong UserId);

/// <summary>
/// Builds a per-interaction layered configuration snapshot and binds typed options from it.
/// <para>
/// One DI scope serves one interaction, so the instance also carries the interaction's own
/// scope: the command boundary calls <see cref="SetScope"/> once, and everything downstream
/// resolves through <see cref="GetCurrentAsync{T}"/> without re-deriving the IDs. Builds are
/// cached per scope, so two consumers in the same interaction share one query and one
/// identical set of values.
/// </para>
/// </summary>
public interface IInteractionConfiguration
{
    /// <summary>
    /// The scope this instance resolves by default; <c>null</c> until the boundary sets it.
    /// </summary>
    InteractionScope? CurrentScope { get; }

    /// <summary>
    /// Binds this instance to the interaction's scope. Called once at the command boundary;
    /// setting a different scope afterwards is a programming error.
    /// </summary>
    void SetScope(InteractionScope scope);

    /// <summary>
    /// Base configuration plus global, guild, channel, and user overrides, broadest first.
    /// </summary>
    Task<IConfiguration> BuildAsync(InteractionScope scope, CancellationToken ct = default);

    /// <summary>
    /// Binds the registered section for <typeparamref name="T"/> from the snapshot for
    /// <paramref name="scope"/>. Use for targets that are not the current interaction (for
    /// example validating a write aimed at another scope).
    /// </summary>
    Task<T> GetAsync<T>(InteractionScope scope, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Binds the registered section for <typeparamref name="T"/> from this interaction's
    /// snapshot. Throws when the boundary has not called <see cref="SetScope"/>.
    /// </summary>
    Task<T> GetCurrentAsync<T>(CancellationToken ct = default) where T : class;

    /// <summary>
    /// The snapshot for this interaction. Throws when the boundary has not called
    /// <see cref="SetScope"/>.
    /// </summary>
    Task<IConfiguration> BuildCurrentAsync(CancellationToken ct = default);
}
