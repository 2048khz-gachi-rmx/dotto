using Dotto.Ai.Settings;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>Production defaults to gVisor; Development pins it off.</summary>
public class SandboxConfigurationTests
{
    private static SandboxOptions Bind(string environment)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(TestContext.CurrentContext.TestDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .Build();

        return configuration.GetSection("Ai:Sandbox").Get<SandboxOptions>()
               ?? throw new InvalidOperationException("Ai:Sandbox did not bind");
    }

    [Test]
    public void ProductionConfiguration_UsesGVisorByDefault()
        => Bind("Production").UseGVisorRuntime.ShouldBeTrue();

    [Test]
    public void DevelopmentConfiguration_DisablesGVisor()
        => Bind("Development").UseGVisorRuntime.ShouldBeFalse();
}
