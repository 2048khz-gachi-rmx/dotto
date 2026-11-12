using Dotto.Application.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dotto.Tests.Mocks;

/// <summary>
/// Stands in for the interaction snapshot in tests that exercise a consumer of
/// <see cref="IInteractionConfiguration"/> without a database.
/// </summary>
public sealed class StubInteractionConfiguration : IInteractionConfiguration
{
    private readonly Dictionary<Type, object> _values = [];
    private InteractionScope? _currentScope;

    /// <summary>Number of times a snapshot was built; lets tests assert resolution is cached.</summary>
    public int BuildCount { get; private set; }

    public InteractionScope? CurrentScope => _currentScope;

    public StubInteractionConfiguration With<T>(T value) where T : class
    {
        _values[typeof(T)] = value;
        return this;
    }

    public void SetScope(InteractionScope scope)
    {
        if (_currentScope is { } existing && existing != scope)
            throw new InvalidOperationException("The scope is already bound to a different interaction.");

        _currentScope = scope;
    }

    public Task<IConfiguration> BuildAsync(InteractionScope scope, CancellationToken ct = default)
    {
        BuildCount++;
        return Task.FromResult<IConfiguration>(new ConfigurationBuilder().Build());
    }

    public Task<IConfiguration> BuildCurrentAsync(CancellationToken ct = default)
        => BuildAsync(RequireScope(), ct);

    public Task<T> GetAsync<T>(InteractionScope scope, CancellationToken ct = default) where T : class
        => Task.FromResult(_values.TryGetValue(typeof(T), out var value)
            ? (T)value
            : throw new InvalidOperationException($"No stub value for {typeof(T).Name}."));

    public Task<T> GetCurrentAsync<T>(CancellationToken ct = default) where T : class
        => GetAsync<T>(RequireScope(), ct);

    private InteractionScope RequireScope()
        => _currentScope
           ?? throw new InvalidOperationException("No interaction scope is set.");
}
