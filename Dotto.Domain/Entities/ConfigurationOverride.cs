namespace Dotto.Domain.Entities;

/// <summary>
/// Scope an override applies to, from broadest to narrowest.
/// </summary>
public enum ConfigurationScope
{
    Global = 0,
    Guild = 1,
    Channel = 2,
    User = 3
}

/// <summary>
/// A stored configuration override for a bot instance.
/// </summary>
/// <remarks>
/// <see cref="TargetId"/> is never null: global rows use <c>0</c>, because PostgreSQL
/// treats NULLs as distinct in unique indexes. Discord IDs are never zero.
/// </remarks>
public record ConfigurationOverride
{
    public int Id { get; init; }

    /// <summary>Bot instance this override belongs to (<c>Bot:InstanceName</c>).</summary>
    public required string InstanceName { get; init; }

    public required ConfigurationScope Scope { get; init; }

    /// <summary>Guild/channel/user ID, or <c>0</c> for <see cref="ConfigurationScope.Global"/>.</summary>
    public required ulong TargetId { get; init; }

    /// <summary>Normalized (lowercase) configuration key, e.g. <c>ai:chatassistant:model</c>.</summary>
    public required string Key { get; init; }

    public required string Value { get; set; }

    public required DateTime CreatedOn { get; init; }

    public DateTime UpdatedOn { get; set; }
}
