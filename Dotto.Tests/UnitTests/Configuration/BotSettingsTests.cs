using Dotto.Application.Settings;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Dotto.Tests.UnitTests.Configuration;

public class BotSettingsTests
{
    [Test]
    public void EmptyConfiguration_UsesTheDefaultInstanceNameAndNoOwners()
    {
        var settings = new BotSettings();
        new ConfigurationBuilder().Build().GetSection("Bot").Bind(settings);

        settings.InstanceName.ShouldBe("dotto");
        settings.OwnerIds.ShouldBeEmpty();
    }

    [Test]
    public void ExplicitConfiguration_BindsInstanceNameAndOwners()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bot:InstanceName"] = "other-instance",
                ["Bot:OwnerIds:0"] = "111",
                ["Bot:OwnerIds:1"] = "222"
            })
            .Build();

        var settings = configuration.GetSection("Bot").Get<BotSettings>();

        settings.ShouldNotBeNull();
        settings!.InstanceName.ShouldBe("other-instance");
        settings.OwnerIds.ShouldBe([111UL, 222UL]);
    }
}
