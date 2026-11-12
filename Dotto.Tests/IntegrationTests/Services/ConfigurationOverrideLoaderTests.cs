using Dotto.Ai.Settings;
using Dotto.Application.Configuration;
using Dotto.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Dotto.Tests.IntegrationTests.Services;

public class ConfigurationOverrideLoaderTests : TestDatabaseFixtureBase
{
    [Test]
    public async Task LoadGlobalOverrides_AddsRowsToTheConfiguration_AndTypedOptionsSeeThem()
    {
        var now = TestDateTimeProvider.SetNow();

        DbContext.ConfigurationOverrides.Add(new ConfigurationOverride
        {
            InstanceName = "dotto",
            Scope = ConfigurationScope.Global,
            TargetId = 0,
            Key = "ai:chatassistant:model",
            Value = "global-model",
            CreatedOn = now,
            UpdatedOn = now
        });
        await DbContext.SaveChangesAsync();

        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:BaseUrl"] = "https://example/v1",
                ["Ai:ApiKey"] = "key",
                ["Ai:AssistantModel"] = "base-model",
                ["Ai:ChatAssistant:Model"] = "base-assistant-model"
            });

        var loaded = await ServiceProvider.LoadGlobalOverridesAsync(builder);

        loaded.ShouldBe(1);

        var configuration = builder.Build();
        var settings = configuration.GetSection("Ai").Get<AiSettings>();

        settings.ShouldNotBeNull();
        settings!.ChatAssistant.Model.ShouldBe("global-model");
    }

    [Test]
    public async Task LoadGlobalOverrides_IgnoresOtherInstances()
    {
        var now = TestDateTimeProvider.SetNow();

        DbContext.ConfigurationOverrides.Add(new ConfigurationOverride
        {
            InstanceName = "other",
            Scope = ConfigurationScope.Global,
            TargetId = 0,
            Key = "ai:chatassistant:model",
            Value = "other-model",
            CreatedOn = now,
            UpdatedOn = now
        });
        await DbContext.SaveChangesAsync();

        var builder = new ConfigurationBuilder();
        var loaded = await ServiceProvider.LoadGlobalOverridesAsync(builder);

        loaded.ShouldBe(0);
        builder.Build()["ai:chatassistant:model"].ShouldBeNull();
    }
}
