using Dotto.Application.Configuration;
using Dotto.Common.Configuration;
using Dotto.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Dotto.Tests.UnitTests.Configuration;

public class InteractionConfigurationTests
{
    public class TestOptions
    {
        public string? Value { get; set; }
    }

    private sealed class FakeStore : IConfigurationOverrideStore
    {
        public List<ConfigurationOverride> Rows { get; } = [];

        public string InstanceName => "dotto";

        public int QueryCount { get; private set; }

        public Task<IReadOnlyList<ConfigurationOverride>> GetEffectiveAsync(
            ulong? guildId, ulong channelId, ulong userId, CancellationToken ct = default)
        {
            QueryCount++;
            IReadOnlyList<ConfigurationOverride> matching = Rows
                .Where(row => row.Scope == ConfigurationScope.Global
                              || (guildId != null && row.Scope == ConfigurationScope.Guild && row.TargetId == guildId)
                              || (channelId != 0 && row.Scope == ConfigurationScope.Channel && row.TargetId == channelId)
                              || (userId != 0 && row.Scope == ConfigurationScope.User && row.TargetId == userId))
                .OrderBy(row => row.Scope)
                .ToList();

            return Task.FromResult(matching);
        }

        public Task<IReadOnlyList<ConfigurationOverride>> ListAsync(
            ConfigurationTarget target, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConfigurationOverride>>([]);

        public Task<ConfigurationOverride?> GetAsync(
            ConfigurationTarget target, string key, CancellationToken ct = default)
            => Task.FromResult<ConfigurationOverride?>(null);

        public Task<bool> SetAsync(
            ConfigurationTarget target, string key, string value, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> DeleteAsync(
            ConfigurationTarget target, string key, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private static IConfiguration BaseConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Test:Value"] = "base"
        })
        .Build();

    private static InteractionConfiguration Build(FakeStore store)
    {
        var registry = new ConfigurationSchemaRegistry(
            [new ConfigurationOptionSchema("Test", typeof(TestOptions))]);

        return new InteractionConfiguration(BaseConfiguration(), store, registry);
    }

    private static ConfigurationOverride Row(ConfigurationScope scope, ulong target, string value) => new()
    {
        InstanceName = "dotto",
        Scope = scope,
        TargetId = target,
        Key = "test:value",
        Value = value,
        CreatedOn = DateTime.UtcNow,
        UpdatedOn = DateTime.UtcNow
    };

    [Test]
    public async Task Build_LayersUserOverChannelOverGuildOverGlobalOverBase()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.Global, 0, "global"));
        store.Rows.Add(Row(ConfigurationScope.Guild, 10, "guild"));
        store.Rows.Add(Row(ConfigurationScope.Channel, 20, "channel"));
        store.Rows.Add(Row(ConfigurationScope.User, 30, "user"));

        var configuration = Build(store);

        var snapshot = await configuration.BuildAsync(new InteractionScope(10, 20, 30));

        snapshot["Test:Value"].ShouldBe("user");
    }

    [Test]
    public async Task Build_FallsBackToBaseConfiguration_WhenNoOverrideMatches()
    {
        var configuration = Build(new FakeStore());

        var snapshot = await configuration.BuildAsync(new InteractionScope(10, 20, 30));

        snapshot["Test:Value"].ShouldBe("base");
    }

    [Test]
    public async Task Build_SkipsGuildLayer_WhenThereIsNoGuild()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.Guild, 10, "guild"));
        store.Rows.Add(Row(ConfigurationScope.Global, 0, "global"));

        var configuration = Build(store);

        var snapshot = await configuration.BuildAsync(new InteractionScope(null, 20, 30));

        snapshot["Test:Value"].ShouldBe("global");
    }

    [Test]
    public async Task Build_ReturnsIndependentSnapshotsPerScope()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.Guild, 10, "ten"));
        store.Rows.Add(Row(ConfigurationScope.Guild, 11, "eleven"));

        var configuration = Build(store);

        var first = await configuration.BuildAsync(new InteractionScope(10, 20, 30));
        var second = await configuration.BuildAsync(new InteractionScope(11, 20, 30));

        first["Test:Value"].ShouldBe("ten");
        second["Test:Value"].ShouldBe("eleven");
    }

    [Test]
    public async Task Get_BindsTypedOptionsFromTheSnapshot()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.User, 30, "overridden"));

        var configuration = Build(store);

        var options = await configuration.GetAsync<TestOptions>(new InteractionScope(10, 20, 30));

        options.Value.ShouldBe("overridden");
    }

    [Test]
    public async Task Get_Throws_ForUnregisteredOptionsType()
    {
        var configuration = Build(new FakeStore());

        await Should.ThrowAsync<InvalidOperationException>(
            () => configuration.GetAsync<UnregisteredOptions>(new InteractionScope(1, 2, 3)));
    }

    public class UnregisteredOptions
    {
        public string? Value { get; set; }
    }

    [Test]
    public async Task CurrentScope_IsUnsetUntilTheBoundaryBindsIt()
    {
        var configuration = Build(new FakeStore());

        configuration.CurrentScope.ShouldBeNull();

        await Should.ThrowAsync<InvalidOperationException>(
            () => configuration.GetCurrentAsync<TestOptions>());
    }

    [Test]
    public async Task CurrentScope_DrivesResolutionAndCannotBeRebound()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.Guild, 10, "guild"));

        var configuration = Build(store);
        var scope = new InteractionScope(10, 20, 30);

        configuration.SetScope(scope);
        var options = await configuration.GetCurrentAsync<TestOptions>();

        options.Value.ShouldBe("guild");

        Should.Throw<InvalidOperationException>(() => configuration.SetScope(new InteractionScope(11, 20, 30)));

        // Idempotent for the same interaction, so a defensive set downstream is harmless.
        configuration.SetScope(scope);
    }

    [Test]
    public async Task RepeatedResolutionsShareOneQueryAndOneSnapshot()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.User, 30, "user"));

        var configuration = Build(store);
        configuration.SetScope(new InteractionScope(10, 20, 30));

        var first = await configuration.GetCurrentAsync<TestOptions>();
        var second = await configuration.GetCurrentAsync<TestOptions>();
        var snapshot = await configuration.BuildCurrentAsync();

        first.ShouldBeSameAs(second);
        store.QueryCount.ShouldBe(1);
        snapshot["Test:Value"].ShouldBe("user");
    }

    [Test]
    public async Task DifferentScopesDoNotShareASnapshot()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.Guild, 10, "ten"));
        store.Rows.Add(Row(ConfigurationScope.Guild, 11, "eleven"));

        var configuration = Build(store);

        var ten = await configuration.GetAsync<TestOptions>(new InteractionScope(10, 20, 30));
        var eleven = await configuration.GetAsync<TestOptions>(new InteractionScope(11, 20, 30));

        ten.Value.ShouldBe("ten");
        eleven.Value.ShouldBe("eleven");
        store.QueryCount.ShouldBe(2);
    }
}
