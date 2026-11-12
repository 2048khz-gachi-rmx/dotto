using Dotto.Application.Configuration;
using Dotto.Application.Settings;
using Dotto.Domain.Entities;
using Dotto.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dotto.Tests.IntegrationTests.Services;

public class ConfigurationOverrideStoreTests : TestDatabaseFixtureBase
{
    private ConfigurationOverrideStore CreateStore(string instanceName = "dotto")
        => new(DbContext, Options.Create(new BotSettings { InstanceName = instanceName }), TestDateTimeProvider);

    [Test]
    public async Task Set_InsertsAndUpsertsWithoutCreatingDuplicates()
    {
        var store = CreateStore();
        var target = ConfigurationTarget.User(42);
        var created = TestDateTimeProvider.SetNow();
        TestDateTimeProvider.SetNow(created);

        var updated = await store.SetAsync(target, "Ai:ChatAssistant:Model", "first");

        updated.ShouldBeFalse();
        var first = await DbContext.ConfigurationOverrides.SingleAsync();
        first.Key.ShouldBe("ai:chatassistant:model");
        first.CreatedOn.ShouldBe(created.ToUniversalTime(), TimeSpan.FromMilliseconds(1));

        var later = TestDateTimeProvider.SetNow(created.AddMinutes(5));
        var replaced = await store.SetAsync(target, "ai:chatassistant:model", "second");

        replaced.ShouldBeTrue();
        var rows = await DbContext.ConfigurationOverrides.ToListAsync();
        rows.Count.ShouldBe(1);
        rows.Single().Value.ShouldBe("second");
        rows.Single().CreatedOn.ShouldBe(created.ToUniversalTime(), TimeSpan.FromMilliseconds(1));
        rows.Single().UpdatedOn.ShouldBe(later.ToUniversalTime(), TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task Lookup_FiltersByScopeAndTarget()
    {
        var store = CreateStore();
        await store.SetAsync(ConfigurationTarget.Global, "a:key", "global");
        await store.SetAsync(ConfigurationTarget.Guild(10), "a:key", "guild");
        await store.SetAsync(ConfigurationTarget.Channel(20), "a:key", "channel");
        await store.SetAsync(ConfigurationTarget.User(30), "a:key", "user");
        await store.SetAsync(ConfigurationTarget.User(31), "a:key", "other-user");

        var effective = await store.GetEffectiveAsync(10, 20, 30);

        effective.Select(row => row.Value).ShouldBe(["global", "guild", "channel", "user"]);

        var withoutGuild = await store.GetEffectiveAsync(null, 20, 30);
        withoutGuild.Select(row => row.Value).ShouldBe(["global", "channel", "user"]);
    }

    [Test]
    public async Task InstancesAreIsolated()
    {
        var firstInstance = CreateStore("first");
        var secondInstance = CreateStore("second");

        await firstInstance.SetAsync(ConfigurationTarget.Global, "a:key", "first-value");

        var secondRows = await secondInstance.ListAsync(ConfigurationTarget.Global);

        secondRows.ShouldBeEmpty();
        (await secondInstance.GetEffectiveAsync(null, 0, 0)).ShouldBeEmpty();
    }

    [Test]
    public async Task Delete_RemovesRowAndReportsWhetherItExisted()
    {
        var store = CreateStore();
        var target = ConfigurationTarget.Guild(7);
        await store.SetAsync(target, "a:key", "value");

        (await store.DeleteAsync(target, "A:KEY")).ShouldBeTrue();
        (await store.GetAsync(target, "a:key")).ShouldBeNull();
        (await store.DeleteAsync(target, "a:key")).ShouldBeFalse();
    }

    [Test]
    public async Task UniqueIndex_RejectsDuplicateInstanceScopeTargetKey()
    {
        var now = TestDateTimeProvider.SetNow();

        DbContext.ConfigurationOverrides.AddRange(
            new ConfigurationOverride
            {
                InstanceName = "dotto",
                Scope = ConfigurationScope.Global,
                TargetId = 0,
                Key = "a:key",
                Value = "one",
                CreatedOn = now,
                UpdatedOn = now
            },
            new ConfigurationOverride
            {
                InstanceName = "dotto",
                Scope = ConfigurationScope.Global,
                TargetId = 0,
                Key = "a:key",
                Value = "two",
                CreatedOn = now,
                UpdatedOn = now
            });

        await Should.ThrowAsync<DbUpdateException>(() => DbContext.SaveChangesAsync());

        // Drop the rejected entities so the fixture's TearDown truncation does not retry them.
        DbContext.ChangeTracker.Clear();
    }
}
