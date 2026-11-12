using Dotto.Common.Configuration;
using Dotto.Domain.Entities;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Dotto.Application.Configuration;

/// <summary>
/// One segment of a resolved key path: a property, a list index, or a dictionary key.
/// </summary>
public readonly record struct ConfigurationPathStep(PropertyInfo? Property, int? Index, string? Key)
{
    public static ConfigurationPathStep ForProperty(PropertyInfo property) => new(property, null, null);

    public static ConfigurationPathStep ForIndex(int index) => new(null, index, null);

    public static ConfigurationPathStep ForKey(string key) => new(null, null, key);
}

/// <summary>
/// Index over the option descriptors registered by each module. Used to map a key to a
/// typed section, a section to a key prefix, and an options type to its section. It is also
/// the only place that knows how a key walks an options type, so validation, autocomplete,
/// and scope-support lookups agree on what a key addresses.
/// </summary>
public sealed class ConfigurationSchemaRegistry
{
    private readonly List<IConfigurationOptionSchema> _schemas;

    public ConfigurationSchemaRegistry(IEnumerable<IConfigurationOptionSchema> schemas)
    {
        _schemas = schemas
            .GroupBy(schema => schema.Section, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public IReadOnlyList<IConfigurationOptionSchema> Schemas => _schemas;

    /// <summary>
    /// Finds the longest registered section that is a prefix of <paramref name="key"/>.
    /// <c>Ai:Sandbox:MemoryMb</c> matches <c>Ai:Sandbox</c>, not <c>Ai</c>.
    /// </summary>
    public IConfigurationOptionSchema? FindSection(string key)
    {
        return _schemas
            .Where(schema => IsPrefix(schema.Section, key))
            .OrderByDescending(schema => schema.Section.Length)
            .FirstOrDefault();
    }

    public IConfigurationOptionSchema? FindSection(Type optionsType)
    {
        return _schemas.FirstOrDefault(schema => schema.OptionsType == optionsType);
    }

    /// <summary>
    /// Resolves a key inside a section to the path that walks its options type. Returns
    /// <c>false</c> when a segment does not name a property (or a valid index/key for the
    /// containing collection).
    /// </summary>
    public bool TryResolvePath(
        Type optionsType,
        string relativePath,
        out IReadOnlyList<ConfigurationPathStep> steps,
        out Type leafType)
    {
        var resolved = ResolvePath(optionsType, relativePath);
        steps = resolved?.Steps ?? [];
        leafType = resolved?.LeafType ?? typeof(object);
        return resolved != null;
    }

    /// <summary>
    /// The scopes an override for <paramref name="key"/> may target. Nearest declaration wins:
    /// the leaf property, then its declaring type, then the closest ancestor property.
    /// </summary>
    public ConfigurationScopeSupport GetScopeSupport(string key)
    {
        var schema = FindSection(key);
        if (schema == null)
            return ConfigurationScopeSupport.Global;

        var relativePath = key.Length == schema.Section.Length
            ? string.Empty
            : key[(schema.Section.Length + 1)..];

        return TryResolvePath(schema.OptionsType, relativePath, out var steps, out _)
            ? GetScopeSupport(schema, steps)
            : ConfigurationScopeSupport.Global;
    }

    /// <summary>
    /// Every scalar key the registered sections define, optionally limited to keys that may be
    /// set at <paramref name="scope"/>. Collections and dictionaries are skipped: overrides
    /// target scalar leaves only, though their elements are writable by index/key.
    /// </summary>
    public IReadOnlyList<string> GetKnownKeys(ConfigurationScope? scope = null)
    {
        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var schema in _schemas)
            CollectKeys(schema.Section, schema.OptionsType, keys, depth: 0);

        return keys
            .Where(key => Supports(key, scope))
            .ToList();
    }

    private bool Supports(string key, ConfigurationScope? scope)
    {
        var support = GetScopeSupport(key);

        if (support == ConfigurationScopeSupport.None)
            return false;

        // Global writes are allowed for every non-reserved key; only scoped targets need the
        // section to be live per interaction.
        return scope is null or ConfigurationScope.Global
               || support.HasFlag(ConfigurationScopeSupport.Scoped);
    }

    private static ConfigurationScopeSupport GetScopeSupport(
        IConfigurationOptionSchema schema, IReadOnlyList<ConfigurationPathStep> steps)
    {
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var property = steps[i].Property;
            if (property == null)
                continue;

            if (property.GetCustomAttribute<ConfigurationScopeSupportAttribute>(inherit: true) is { } onProperty)
                return onProperty.Support;

            if (property.DeclaringType?.GetCustomAttribute<ConfigurationScopeSupportAttribute>(inherit: true)
                is { } onDeclaringType)
            {
                return onDeclaringType.Support;
            }
        }

        return schema.OptionsType.GetCustomAttribute<ConfigurationScopeSupportAttribute>(inherit: true)?.Support
               ?? ConfigurationScopeSupport.Global;
    }

    private static bool IsPrefix(string section, string key)
    {
        return key.Equals(section, StringComparison.OrdinalIgnoreCase)
               || key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectKeys(string prefix, Type type, ISet<string> keys, int depth)
    {
        if (depth > 5)
            return;

        type = Nullable.GetUnderlyingType(type) ?? type;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetSetMethod() == null)
                continue;

            var key = $"{prefix}:{property.Name}";
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (IsScalar(propertyType))
            {
                keys.Add(key);
                continue;
            }

            if (typeof(IEnumerable).IsAssignableFrom(propertyType))
                continue;

            if (propertyType.IsClass)
                CollectKeys(key, propertyType, keys, depth + 1);
        }
    }

    internal static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsPrimitive
               || type.IsEnum
               || type == typeof(string)
               || type == typeof(decimal)
               || type == typeof(TimeSpan)
               || type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(Guid)
               || type == typeof(Uri);
    }

    private static ResolvedPath? ResolvePath(Type optionsType, string relativePath)
    {
        if (relativePath.Length == 0)
            return null;

        var segments = relativePath.Split(':');
        var steps = new List<ConfigurationPathStep>();
        var currentType = optionsType;

        foreach (var segment in segments)
        {
            var dictionaryValueType = TryGetDictionaryValueType(currentType);
            if (dictionaryValueType != null)
            {
                steps.Add(ConfigurationPathStep.ForKey(segment));
                currentType = dictionaryValueType;
                continue;
            }

            var listElementType = TryGetListElementType(currentType);
            if (listElementType != null)
            {
                if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    return null;

                steps.Add(ConfigurationPathStep.ForIndex(index));
                currentType = listElementType;
                continue;
            }

            var property = currentType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                    candidate.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));

            if (property == null)
                return null;

            steps.Add(ConfigurationPathStep.ForProperty(property));
            currentType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }

        return new ResolvedPath(steps, currentType);
    }

    private static Type? TryGetListElementType(Type type)
    {
        if (type == typeof(string))
            return null;

        if (type.IsArray)
            return type.GetElementType();

        if (TryGetGenericListElementType(type) is { } elementType)
            return elementType;

        foreach (var interfaceType in type.GetInterfaces())
        {
            if (TryGetGenericListElementType(interfaceType) is { } interfaceElementType)
                return interfaceElementType;
        }

        return null;
    }

    private static Type? TryGetGenericListElementType(Type type)
    {
        if (!type.IsGenericType)
            return null;

        var definition = type.GetGenericTypeDefinition();

        if (definition == typeof(List<>)
            || definition == typeof(IList<>)
            || definition == typeof(ICollection<>)
            || definition == typeof(IEnumerable<>)
            || definition == typeof(IReadOnlyList<>)
            || definition == typeof(IReadOnlyCollection<>))
        {
            return type.GetGenericArguments()[0];
        }

        return null;
    }

    private static Type? TryGetDictionaryValueType(Type type)
    {
        if (TryGetGenericDictionaryValueType(type) is { } valueType)
            return valueType;

        foreach (var interfaceType in type.GetInterfaces())
        {
            if (TryGetGenericDictionaryValueType(interfaceType) is { } interfaceValueType)
                return interfaceValueType;
        }

        return null;
    }

    private static Type? TryGetGenericDictionaryValueType(Type type)
    {
        if (!type.IsGenericType)
            return null;

        var definition = type.GetGenericTypeDefinition();

        if (definition == typeof(Dictionary<,>)
            || definition == typeof(IDictionary<,>)
            || definition == typeof(IReadOnlyDictionary<,>))
        {
            return type.GetGenericArguments()[1];
        }

        return null;
    }

    private sealed record ResolvedPath(IReadOnlyList<ConfigurationPathStep> Steps, Type LeafType);
}
