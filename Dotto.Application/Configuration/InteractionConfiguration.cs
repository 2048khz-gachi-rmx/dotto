using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace Dotto.Application.Configuration;

internal sealed class InteractionConfiguration(
    IConfiguration hostConfiguration,
    IConfigurationOverrideStore overrideStore,
    ConfigurationSchemaRegistry schemaRegistry) : IInteractionConfiguration
{
    /// <summary>
    /// One query and one configuration root per scope. The cache lives on the scoped instance,
    /// so entries are discarded with the interaction.
    /// </summary>
    private readonly ConcurrentDictionary<InteractionScope, Lazy<Task<IConfiguration>>> _snapshots = new();

    private readonly ConcurrentDictionary<(Type OptionsType, InteractionScope Scope), Lazy<Task<object>>>
        _boundOptions = new();

    private InteractionScope? _currentScope;

    public InteractionScope? CurrentScope => _currentScope;

    public void SetScope(InteractionScope scope)
    {
        if (_currentScope is { } existing && existing != scope)
        {
            throw new InvalidOperationException(
                $"This configuration scope is already bound to {Describe(existing)}; " +
                $"it cannot be rebound to {Describe(scope)}.");
        }

        _currentScope = scope;
    }

    public Task<IConfiguration> BuildAsync(InteractionScope scope, CancellationToken ct = default)
        => GetSharedAsync(_snapshots, scope, () => BuildSnapshotAsync(scope, ct));

    public async Task<T> GetAsync<T>(InteractionScope scope, CancellationToken ct = default)
        where T : class
        => (T)await GetSharedAsync(
            _boundOptions,
            (typeof(T), scope),
            () => BindAsync<T>(scope, ct));

    public Task<IConfiguration> BuildCurrentAsync(CancellationToken ct = default)
        => BuildAsync(RequireScope(), ct);

    public Task<T> GetCurrentAsync<T>(CancellationToken ct = default) where T : class
        => GetAsync<T>(RequireScope(), ct);

    private InteractionScope RequireScope()
        => _currentScope
           ?? throw new InvalidOperationException(
               "No interaction scope is set. The command boundary must call SetScope before " +
               "resolving per-interaction configuration.");

    private async Task<IConfiguration> BuildSnapshotAsync(InteractionScope scope, CancellationToken ct)
    {
        var overrides = await overrideStore.GetEffectiveAsync(
            scope.GuildId,
            scope.ChannelId,
            scope.UserId,
            ct);

        var builder = new ConfigurationBuilder()
            .AddConfiguration(hostConfiguration);

        // The store returns broadest first, so narrower rows are added later and win.
        foreach (var row in overrides.OrderBy(row => row.Scope))
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [row.Key] = row.Value
            });
        }

        return builder.Build();
    }

    private async Task<object> BindAsync<T>(InteractionScope scope, CancellationToken ct) where T : class
    {
        var schema = schemaRegistry.FindSection(typeof(T))
                     ?? throw new InvalidOperationException(
                         $"{typeof(T).Name} is not registered as a configuration section.");

        var snapshot = await BuildAsync(scope, ct);
        return snapshot.GetSection(schema.Section).Get<T>()
               ?? throw new InvalidOperationException(
                   $"Configuration section '{schema.Section}' could not be bound to {typeof(T).Name}.");
    }

    /// <summary>
    /// Runs <paramref name="factory"/> at most once per key; concurrent callers await the same
    /// task. A failed build is evicted so a transient failure does not poison the interaction.
    /// </summary>
    private static async Task<TValue> GetSharedAsync<TKey, TValue>(
        ConcurrentDictionary<TKey, Lazy<Task<TValue>>> cache,
        TKey key,
        Func<Task<TValue>> factory)
        where TKey : notnull
    {
        var lazy = cache.GetOrAdd(key, _ => new Lazy<Task<TValue>>(factory));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            cache.TryRemove(key, out _);
            throw;
        }
    }

    private static string Describe(InteractionScope scope)
        => $"guild {scope.GuildId?.ToString() ?? "none"}, channel {scope.ChannelId}, user {scope.UserId}";
}
