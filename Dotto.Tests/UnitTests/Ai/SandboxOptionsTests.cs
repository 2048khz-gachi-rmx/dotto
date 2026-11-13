using Dotto.Ai;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>Configuration-binding behavior for <see cref="SandboxOptions"/>.</summary>
public class SandboxOptionsTests
{
    private static SandboxOptions Resolve(params string[] dnsServers)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < dnsServers.Length; i++)
            values[$"Ai:Sandbox:DnsServers:{i}"] = dnsServers[i];

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddAi(_ => { });

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SandboxOptions>>().Value;
    }

    [Test]
    public void DnsServers_ConfiguredValues_ReplaceDefault()
    {
        Resolve("9.9.9.9").DnsServers.ShouldBe(new[] { "9.9.9.9" });
    }

    [Test]
    public void DnsServers_Duplicates_AreRemoved()
    {
        Resolve("1.1.1.1", "8.8.8.8", "1.1.1.1")
            .DnsServers.ShouldBe(new[] { "1.1.1.1", "8.8.8.8" });
    }

    [Test]
    public void DnsServers_Unset_FallsBackToPublicResolvers()
    {
        Resolve().DnsServers.ShouldBe(new[] { "1.1.1.1", "8.8.8.8" });
    }
}
