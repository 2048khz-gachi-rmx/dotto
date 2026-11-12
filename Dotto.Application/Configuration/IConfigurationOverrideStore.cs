using Dotto.Domain.Entities;

namespace Dotto.Application.Configuration;

/// <summary>
/// Address of a scope target. Global ignores <see cref="TargetId"/>.
/// </summary>
public readonly record struct ConfigurationTarget(ConfigurationScope Scope, ulong TargetId)
{
    public static ConfigurationTarget Global { get; } = new(ConfigurationScope.Global, 0);

    public static ConfigurationTarget Guild(ulong guildId) => new(ConfigurationScope.Guild, guildId);

    public static ConfigurationTarget Channel(ulong channelId) => new(ConfigurationScope.Channel, channelId);

    public static ConfigurationTarget User(ulong userId) => new(ConfigurationScope.User, userId);
}

/// <summary>
/// Persistence for configuration overrides, partitioned by <c>Bot:InstanceName</c>.
/// Keys are normalized to lowercase.
/// </summary>
public interface IConfigurationOverrideStore
{
    string InstanceName { get; }

    /// <summary>
    /// Overrides that apply to an interaction, broadest first (global, guild, channel, user).
    /// </summary>
    Task<IReadOnlyList<ConfigurationOverride>> GetEffectiveAsync(
        ulong? guildId, ulong channelId, ulong userId, CancellationToken ct = default);

    Task<IReadOnlyList<ConfigurationOverride>> ListAsync(
        ConfigurationTarget target, CancellationToken ct = default);

    Task<ConfigurationOverride?> GetAsync(
        ConfigurationTarget target, string key, CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces the value for the target and key. Returns whether a row was updated.
    /// </summary>
    Task<bool> SetAsync(
        ConfigurationTarget target, string key, string value, CancellationToken ct = default);

    /// <summary>Deletes the row. Returns whether a row was removed.</summary>
    Task<bool> DeleteAsync(ConfigurationTarget target, string key, CancellationToken ct = default);
}
