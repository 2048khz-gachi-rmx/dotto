using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Dotto.Common.Configuration;
using Dotto.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace Dotto.Application.Configuration;

public sealed record ConfigurationValueValidationResult(
    bool IsValid,
    string? Error = null,
    string? Warning = null)
{
    public static ConfigurationValueValidationResult Valid(string? warning = null) => new(true, Warning: warning);

    public static ConfigurationValueValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// Validates a key/value before it is stored. Only keys that address a scalar leaf of a
/// registered options type are accepted. Known properties are converted with their type
/// converter and validated with <see cref="Validator"/>; the key's
/// <see cref="ConfigurationScopeSupport"/> decides which scopes may target it.
/// </summary>
public interface IConfigurationValueValidator
{
    ConfigurationValueValidationResult Validate(
        string key,
        string value,
        ConfigurationScope targetScope,
        IConfiguration baseConfiguration);
}

internal sealed class ConfigurationValueValidator(ConfigurationSchemaRegistry schemaRegistry)
    : IConfigurationValueValidator
{
    private static readonly Regex KeyPattern = new(
        @"^[A-Za-z0-9_\-]+(:[A-Za-z0-9_\-]+)*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Sections that change the bot's identity or data source. Overriding them through the
    /// store would let an owner lock everyone (including themselves) out, so they are rejected
    /// before key resolution. Bound at startup from the environment or appsettings.
    /// </summary>
    private static readonly string[] ReservedSections = ["Bot", "ConnectionString"];

    public ConfigurationValueValidationResult Validate(
        string key, string value, ConfigurationScope targetScope, IConfiguration baseConfiguration)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ConfigurationValueValidationResult.Invalid("Key cannot be empty.");

        key = key.Trim();

        if (!KeyPattern.IsMatch(key))
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{key}' is not a valid configuration key. " +
                "Use colon-separated segments made of letters, digits, underscores, and hyphens.");
        }

        if (IsReserved(key))
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{key}' is reserved: the bot's identity and database connection are configured " +
                "at startup and cannot be overridden at runtime.");
        }

        var schema = schemaRegistry.FindSection(key);
        if (schema == null)
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{key}' is not a known configuration key. " +
                $"Pick one of the registered sections: {DescribeSections()}.");
        }

        var relativePath = key.Length == schema.Section.Length
            ? string.Empty
            : key[(schema.Section.Length + 1)..];

        if (relativePath.Length == 0)
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{key}' is a section, not a value. Set a key inside it (e.g. {key}:SomeProperty).");
        }

        if (!schemaRegistry.TryResolvePath(schema.OptionsType, relativePath, out var steps, out var leafType))
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{key}' matches section '{schema.Section}' but no such property exists." +
                DescribeSuggestions(key, schema.Section));
        }

        var scopeSupport = schemaRegistry.GetScopeSupport(key);
        if (scopeSupport == ConfigurationScopeSupport.None)
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{key}' cannot be overridden.");
        }

        if (targetScope != ConfigurationScope.Global
            && !scopeSupport.HasFlag(ConfigurationScopeSupport.Scoped))
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{key}' is read once at startup, so a {Describe(targetScope)} override would have " +
                "no effect. Set it globally (scope=global); it applies after a restart.");
        }

        if (!ConfigurationSchemaRegistry.IsScalar(leafType))
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{key}' is not a scalar value; address a leaf (collections use index/key segments).");
        }

        var converter = TypeDescriptor.GetConverter(leafType);
        if (!converter.CanConvertFrom(typeof(string)))
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{key}' cannot be converted from text.");
        }

        try
        {
            converter.ConvertFromInvariantString(value);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
        {
            return ConfigurationValueValidationResult.Invalid(
                $"'{value}' is not a valid {leafType.Name} for '{key}'.");
        }

        var annotations = ValidateAnnotations(schema, steps, key, value, baseConfiguration);
        if (!annotations.IsValid)
            return annotations;

        return scopeSupport == ConfigurationScopeSupport.Global
            ? ConfigurationValueValidationResult.Valid(
                $"'{key}' is read once at startup; the new value applies after a restart.")
            : ConfigurationValueValidationResult.Valid(annotations.Warning);
    }

    private static ConfigurationValueValidationResult ValidateAnnotations(
        IConfigurationOptionSchema schema,
        IReadOnlyList<ConfigurationPathStep> steps,
        string key,
        string value,
        IConfiguration baseConfiguration)
    {
        var candidate = new ConfigurationBuilder()
            .AddConfiguration(baseConfiguration)
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        object? section;
        try
        {
            section = candidate.GetSection(schema.Section).Get(schema.OptionsType);
        }
        catch (Exception ex)
        {
            return ConfigurationValueValidationResult.Invalid(
                $"Could not bind '{schema.Section}' with this value: {ex.Message}");
        }

        var leafStep = steps[^1];
        var declaringPath = steps.Take(steps.Count - 1).ToList();
        var declaringObject = declaringPath.Count == 0
            ? section
            : Navigate(section, declaringPath);

        if (declaringObject == null)
        {
            // The property is valid but its declaring object could not be materialized
            // (for example, no other value for that subsection is configured).
            return ConfigurationValueValidationResult.Valid();
        }

        // A collection element or dictionary value is the leaf itself; validate it directly.
        if (leafStep.Property == null)
        {
            var element = Navigate(section, steps);
            return ValidateObject(element, memberName: null);
        }

        return ValidateObject(declaringObject, leafStep.Property.Name);
    }

    private static ConfigurationValueValidationResult ValidateObject(object? instance, string? memberName)
    {
        if (instance == null)
            return ConfigurationValueValidationResult.Valid();

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            instance,
            new ValidationContext(instance),
            results,
            validateAllProperties: true);

        var errors = results
            .Where(result => memberName == null
                             || result.MemberNames.Any(member =>
                                 member.Equals(memberName, StringComparison.OrdinalIgnoreCase)))
            .Select(result => result.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList();

        return errors.Count == 0
            ? ConfigurationValueValidationResult.Valid()
            : ConfigurationValueValidationResult.Invalid(string.Join(" ", errors));
    }

    private static object? Navigate(object? instance, IReadOnlyList<ConfigurationPathStep> path)
    {
        foreach (var step in path)
        {
            if (instance == null)
                return null;

            if (step.Property != null)
            {
                instance = step.Property.GetValue(instance);
                continue;
            }

            if (step.Index is { } index)
            {
                instance = instance switch
                {
                    Array array => index < array.Length ? array.GetValue(index) : null,
                    IList list => index < list.Count ? list[index] : null,
                    _ => null
                };
                continue;
            }

            if (step.Key != null)
            {
                instance = instance is IDictionary dictionary && dictionary.Contains(step.Key)
                    ? dictionary[step.Key]
                    : null;
            }
        }

        return instance;
    }

    private static bool IsReserved(string key)
    {
        return ReservedSections.Any(section =>
            key.Equals(section, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase));
    }

    private string DescribeSections()
    {
        return string.Join(", ", schemaRegistry.Schemas.Select(schema => schema.Section));
    }

    private string DescribeSuggestions(string key, string section)
    {
        var separator = key.LastIndexOf(':');
        var parent = separator > section.Length ? key[..separator] : section;

        var suggestions = schemaRegistry.GetKnownKeys()
            .Where(candidate => candidate.StartsWith(parent + ":", StringComparison.OrdinalIgnoreCase)
                                || candidate.Equals(parent, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        return suggestions.Count == 0
            ? string.Empty
            : $" Did you mean: {string.Join(", ", suggestions)}?";
    }

    private static string Describe(ConfigurationScope scope) => scope switch
    {
        ConfigurationScope.Guild => "guild",
        ConfigurationScope.Channel => "channel",
        ConfigurationScope.User => "user",
        _ => "scoped"
    };
}
