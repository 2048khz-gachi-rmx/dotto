namespace Dotto.Common.Configuration;

/// <summary>
/// Describes a configuration section that an options type is bound from.
/// </summary>
public interface IConfigurationOptionSchema
{
    /// <summary>Configuration section, e.g. <c>Ai</c>, <c>Ai:Sandbox</c>, <c>Minio</c>.</summary>
    string Section { get; }

    /// <summary>Type the section binds to.</summary>
    Type OptionsType { get; }
}

public sealed record ConfigurationOptionSchema(string Section, Type OptionsType) : IConfigurationOptionSchema;
