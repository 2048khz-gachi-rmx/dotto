using Dotto.Application.Configuration;
using Dotto.Common.Configuration;
using Dotto.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Dotto.Tests.UnitTests.Configuration;

public class ConfigurationValueValidatorTests
{
    public class ValidatedOptions
    {
        [System.ComponentModel.DataAnnotations.Range(1, 10)]
        public int Number { get; set; } = 5;

        public string? Name { get; set; }

        public List<string> Items { get; set; } = [];

        public List<Rule> Rules { get; set; } = [];

        public Dictionary<string, string> Map { get; set; } = [];

        /// <summary>Resolved per interaction, so scoped overrides are accepted.</summary>
        [ConfigurationScopeSupport(ConfigurationScopeSupport.Any)]
        public string? Live { get; set; }

        public TunnelOptions Tunnel { get; set; } = new();
    }

    public class Rule
    {
        [System.ComponentModel.DataAnnotations.Range(1, 10)]
        public int Number { get; set; } = 5;

        public List<string> Patterns { get; set; } = [];
    }

    [ConfigurationScopeSupport(ConfigurationScopeSupport.None)]
    public class TunnelOptions
    {
        public string? Target { get; set; }
    }

    private static ConfigurationValueValidator Build()
    {
        var registry = new ConfigurationSchemaRegistry(
            [new ConfigurationOptionSchema("Opt", typeof(ValidatedOptions))]);

        return new ConfigurationValueValidator(registry);
    }

    private static IConfiguration Base() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Opt:Number"] = "5",
            ["Opt:Name"] = "base",
            ["Opt:Rules:0:Number"] = "5",
            ["Opt:Rules:0:Patterns:0"] = "base-pattern",
            ["Opt:Map:foo"] = "base-map"
        })
        .Build();

    [Test]
    public void Validate_AcceptsAValidScalar()
    {
        var result = Build().Validate("Opt:Number", "7", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void Validate_WarnsThatAStartupBoundGlobalWriteNeedsARestart()
    {
        var result = Build().Validate("Opt:Number", "7", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeTrue();
        result.Warning.ShouldNotBeNull();
        result.Warning.ShouldContain("restart");
    }

    [Test]
    public void Validate_DoesNotWarnForPerInteractionKeys()
    {
        var result = Build().Validate("Opt:Live", "hello", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeTrue();
        result.Warning.ShouldBeNull();
    }

    [Test]
    public void Validate_AcceptsScopedWritesForPerInteractionKeys()
    {
        foreach (var scope in new[]
                 {
                     ConfigurationScope.Guild, ConfigurationScope.Channel, ConfigurationScope.User
                 })
        {
            var result = Build().Validate("Opt:Live", "hello", scope, Base());

            result.IsValid.ShouldBeTrue();
            result.Warning.ShouldBeNull();
        }
    }

    [Test]
    public void Validate_RejectsScopedWritesForStartupBoundKeys()
    {
        var result = Build().Validate("Opt:Number", "7", ConfigurationScope.Guild, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldContain("startup");
        result.Error.ShouldContain("global");
    }

    [Test]
    public void Validate_RejectsKeysMarkedAsNotOverridable()
    {
        var result = Build().Validate("Opt:Tunnel:Target", "paste.rs", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldContain("cannot be overridden");
    }

    [Test]
    public void Validate_RejectsNonNumericValueForNumericProperty()
    {
        var result = Build().Validate("Opt:Number", "abc", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldContain("not a valid");
    }

    [Test]
    public void Validate_RejectsDataAnnotationViolations()
    {
        var result = Build().Validate("Opt:Number", "11", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Validate_RejectsNonScalarProperties()
    {
        var result = Build().Validate("Opt:Items", "a,b", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldContain("scalar");
    }

    [Test]
    public void Validate_RejectsUnknownSections()
    {
        var result = Build().Validate("SomeOther:Key", "anything", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldContain("not a known configuration key");
        result.Error.ShouldContain("Opt");
    }

    [Test]
    public void Validate_RejectsMissingPropertiesUnderKnownNotNullableSections()
    {
        var result = Build().Validate("Opt:Missing", "anything", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldContain("no such property");
        result.Error.ShouldContain("Opt:Number");
    }

    [TestCase("Bot:OwnerIds:0")]
    [TestCase("Bot:InstanceName")]
    [TestCase("bot:instanceName")]
    [TestCase("ConnectionString")]
    public void Validate_RejectsReservedIdentitySections(string key)
    {
        var result = Build().Validate(key, "value", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldContain("reserved");
    }

    [Test]
    public void Validate_AcceptsIndexedListLeaves()
    {
        Build().Validate("Opt:Rules:0:Patterns:0", "replacement", ConfigurationScope.Global, Base())
            .IsValid.ShouldBeTrue();
        Build().Validate("Opt:Rules:0:Number", "7", ConfigurationScope.Global, Base())
            .IsValid.ShouldBeTrue();
    }

    [Test]
    public void Validate_AppliesAnnotationsToIndexedElements()
    {
        var result = Build().Validate("Opt:Rules:0:Number", "11", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Validate_RejectsIndexingAScalarListWithoutAYieldIndex()
    {
        Build().Validate("Opt:Rules:0", "5", ConfigurationScope.Global, Base()).IsValid.ShouldBeFalse();
    }

    [Test]
    public void Validate_AcceptsDictionaryValues()
    {
        Build().Validate("Opt:Map:foo", "replacement", ConfigurationScope.Global, Base())
            .IsValid.ShouldBeTrue();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Opt::Name")]
    [TestCase("Opt Name")]
    [TestCase(":Opt")]
    public void Validate_RejectsMalformedKeys(string key)
    {
        var result = Build().Validate(key, "value", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
    }

    [Test]
    public void Validate_RejectsSectionKeysWithoutAProperty()
    {
        var result = Build().Validate("Opt", "value", ConfigurationScope.Global, Base());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldContain("not a value");
    }
}
