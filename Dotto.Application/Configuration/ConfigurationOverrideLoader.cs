using Dotto.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dotto.Application.Configuration;

public static class ConfigurationOverrideLoader
{
    /// <summary>
    /// Adds this instance's global overrides to the host configuration, so services that
    /// read configuration once during startup observe them. Call after migrations.
    /// </summary>
    /// <returns>Number of global overrides that were applied.</returns>
    public static async Task<int> LoadGlobalOverridesAsync(
        this IServiceProvider services,
        IConfigurationBuilder configuration,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<IConfigurationOverrideStore>();
        var overrides = await store.ListAsync(ConfigurationTarget.Global, ct);

        if (overrides.Count == 0)
            return 0;

        configuration.AddInMemoryCollection(overrides
            .OrderBy(row => row.Key)
            .ToDictionary(row => row.Key, row => (string?)row.Value));

        return overrides.Count;
    }
}
