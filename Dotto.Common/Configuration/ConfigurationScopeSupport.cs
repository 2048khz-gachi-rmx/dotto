namespace Dotto.Common.Configuration;

/// <summary>
/// Which override scopes a configuration key may be set at.
/// </summary>
[Flags]
public enum ConfigurationScopeSupport
{
    /// <summary>Reserved: the key cannot be overridden at all.</summary>
    None = 0,

    /// <summary>Only a global override is allowed, and it is read once at startup.</summary>
    Global = 1,

    /// <summary>The key is resolved per interaction, so any scope is allowed.</summary>
    Scoped = 2,

    /// <summary>Both global and scoped overrides are allowed; scoped values are live.</summary>
    Any = Global | Scoped
}

/// <summary>
/// Marks an options type or property with the scopes an override may target.
/// Resolution walks the edited key outward: the leaf property wins, then its declaring
/// type, then the nearest ancestor property (for collection/dictionary leaves). Keys that
/// resolve to nothing declared are <see cref="ConfigurationScopeSupport.Global"/>: settings
/// are startup-bound unless a consumer opts in by resolving them per interaction.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = true)]
public sealed class ConfigurationScopeSupportAttribute(ConfigurationScopeSupport support) : Attribute
{
    public ConfigurationScopeSupport Support { get; } = support;
}
