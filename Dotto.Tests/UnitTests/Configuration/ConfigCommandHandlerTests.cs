using Dotto.Application.Configuration;
using Dotto.Common.Configuration;
using Dotto.Discord.CommandHandlers.Config;
using Dotto.Discord.Commands.Config;
using Dotto.Discord.Services;
using Dotto.Domain.Entities;
using Microsoft.Extensions.Configuration;
using NetCord.Rest;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.UnitTests.Configuration;

public class ConfigCommandHandlerTests
{
    public class TestOptions
    {
        public string? Value { get; set; }

        public string? ApiKey { get; set; }
    }

    private readonly IBotOwnerService _owner = Substitute.For<IBotOwnerService>();
    private readonly IConfigurationOverrideStore _store = Substitute.For<IConfigurationOverrideStore>();
    private readonly IConfigurationValueValidator _validator = Substitute.For<IConfigurationValueValidator>();
    private readonly IInteractionConfiguration _interactionConfiguration =
        Substitute.For<IInteractionConfiguration>();

    [SetUp]
    public void SetUpDefaults()
    {
        _owner.IsOwnerAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(true);
        _validator.Validate(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConfigurationScope>(),
                Arg.Any<IConfiguration>())
            .Returns(ConfigurationValueValidationResult.Valid());

        _interactionConfiguration.BuildAsync(Arg.Any<InteractionScope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IConfiguration>(new ConfigurationBuilder().Build()));
    }

    private ConfigCommandHandler Build()
    {
        var registry = new ConfigurationSchemaRegistry(
            [new ConfigurationOptionSchema("Test", typeof(TestOptions))]);

        return new ConfigCommandHandler(
            _owner,
            _store,
            _validator,
            _interactionConfiguration,
            registry);
    }

    [Test]
    public async Task Set_StoresAValidatedValue()
    {
        _store.SetAsync(Arg.Any<ConfigurationTarget>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await Build().SetAsync<InteractionMessageProperties>(
            1, 2, 3, ConfigurationScope.Guild, null, "Test:Value", "hello");

        result.Content.ShouldContain("Set `Test:Value`");
        await _store.Received(1).SetAsync(
            Arg.Is<ConfigurationTarget>(target =>
                target.Scope == ConfigurationScope.Guild && target.TargetId == 2),
            "Test:Value",
            "hello",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Set_TargetsAnotherUser_WhenUserScopeIsRequested()
    {
        _store.SetAsync(Arg.Any<ConfigurationTarget>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        await Build().SetAsync<InteractionMessageProperties>(
            1, 2, 3, ConfigurationScope.User, 999, "Test:Value", "hello");

        await _store.Received(1).SetAsync(
            Arg.Is<ConfigurationTarget>(target =>
                target.Scope == ConfigurationScope.User && target.TargetId == 999),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Set_DefaultsUserScopeToTheInvoker()
    {
        _store.SetAsync(Arg.Any<ConfigurationTarget>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        await Build().SetAsync<InteractionMessageProperties>(
            1, 2, 3, ConfigurationScope.User, null, "Test:Value", "hello");

        await _store.Received(1).SetAsync(
            Arg.Is<ConfigurationTarget>(target =>
                target.Scope == ConfigurationScope.User && target.TargetId == 1),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NonOwner_IsRefused_AndNothingIsStored()
    {
        _owner.IsOwnerAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Build().SetAsync<InteractionMessageProperties>(
            1, 2, 3, ConfigurationScope.Global, null, "Test:Value", "hello");

        result.Content.ShouldContain("Only the bot owner");
        await _store.DidNotReceiveWithAnyArgs()
            .SetAsync(default, default!, default!, default);
    }

    [Test]
    public async Task InvalidValue_IsRejected_AndNothingIsStored()
    {
        _validator.Validate(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConfigurationScope>(),
                Arg.Any<IConfiguration>())
            .Returns(ConfigurationValueValidationResult.Invalid("nope"));

        var result = await Build().SetAsync<InteractionMessageProperties>(
            1, 2, 3, ConfigurationScope.Global, null, "Test:Value", "hello");

        result.Content.ShouldContain("nope");
        await _store.DidNotReceiveWithAnyArgs()
            .SetAsync(default, default!, default!, default);
    }

    [Test]
    public async Task Set_IncludesWarningForUnknownKey()
    {
        _validator.Validate(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConfigurationScope>(),
                Arg.Any<IConfiguration>())
            .Returns(ConfigurationValueValidationResult.Valid("unrecognized key"));

        var result = await Build().SetAsync<InteractionMessageProperties>(
            1, 2, 3, ConfigurationScope.Global, null, "Test:Value", "hello");

        result.Content.ShouldContain("unrecognized key");
    }

    [Test]
    public async Task Get_MasksSecretValues()
    {
        _store.GetAsync(Arg.Any<ConfigurationTarget>(), "Test:ApiKey", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ConfigurationOverride?>(new ConfigurationOverride
            {
                InstanceName = "dotto",
                Scope = ConfigurationScope.Global,
                TargetId = 0,
                Key = "test:apikey",
                Value = "super-secret",
                CreatedOn = DateTime.UtcNow,
                UpdatedOn = DateTime.UtcNow
            }));

        var result = await Build().GetAsync<InteractionMessageProperties>(
            1, null, 3, ConfigurationScope.Global, null, "Test:ApiKey");

        result.Content.ShouldContain("***");
        result.Content.ShouldNotContain("super-secret");
    }

    [Test]
    public async Task List_MasksSecretValues()
    {
        _store.ListAsync(Arg.Any<ConfigurationTarget>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ConfigurationOverride>>(
            [
                Row("test:value", "visible"),
                Row("test:apikey", "super-secret")
            ]));

        var result = await Build().ListAsync<InteractionMessageProperties>(
            1, null, 3, ConfigurationScope.Global, null);

        result.Content.ShouldContain("visible");
        result.Content.ShouldContain("***");
        result.Content.ShouldNotContain("super-secret");
    }

    [Test]
    public async Task Get_ReportsUnsetKey()
    {
        _store.GetAsync(Arg.Any<ConfigurationTarget>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ConfigurationOverride?>(null));

        var result = await Build().GetAsync<InteractionMessageProperties>(
            1, null, 3, ConfigurationScope.Global, null, "Test:Value");

        result.Content.ShouldContain("not set");
    }

    [Test]
    public async Task Unset_DeletesAndReports()
    {
        _validator.ClearReceivedCalls();

        _store.DeleteAsync(Arg.Any<ConfigurationTarget>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await Build().UnsetAsync<InteractionMessageProperties>(
            1, null, 3, ConfigurationScope.Global, null, "Test:Value");

        result.Content.ShouldContain("Unset `Test:Value`");

        // Rows for keys a later build renamed or removed must stay removable.
        _validator.DidNotReceiveWithAnyArgs().Validate(default!, default!, default, default!);
    }

    [Test]
    public void Autocomplete_ReturnsRegisteredKnownKeysFilteredByInput()
    {
        var provider = new Dotto.Discord.Commands.Config.ApplicationCommand.ConfigKeyAutocompleteProvider(Build());

        var choices = provider.GetChoices("api").ToList();

        choices.Count.ShouldBe(1);
        choices.Single().Name.ShouldBe("Test:ApiKey");
    }

    [Test]
    public async Task Set_ValidatesAgainstTheTargetScope()
    {
        _validator.ClearReceivedCalls();

        await Build().SetAsync<InteractionMessageProperties>(
            1, 2, 3, ConfigurationScope.Guild, null, "Test:Value", "hello");

        _validator.Received(1).Validate(
            "Test:Value", "hello", ConfigurationScope.Guild, Arg.Any<IConfiguration>());
    }

    [Test]
    public async Task Get_MasksS3Credentials()
    {
        _store.GetAsync(Arg.Any<ConfigurationTarget>(), "Minio:SecretKey", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ConfigurationOverride?>(
                Row("minio:secretkey", "minio-secret")));

        var result = await Build().GetAsync<InteractionMessageProperties>(
            1, null, 3, ConfigurationScope.Global, null, "Minio:SecretKey");

        result.Content.ShouldContain("***");
        result.Content.ShouldNotContain("minio-secret");
    }

    private static ConfigurationOverride Row(string key, string value) => new()
    {
        InstanceName = "dotto",
        Scope = ConfigurationScope.Global,
        TargetId = 0,
        Key = key,
        Value = value,
        CreatedOn = DateTime.UtcNow,
        UpdatedOn = DateTime.UtcNow
    };
}
