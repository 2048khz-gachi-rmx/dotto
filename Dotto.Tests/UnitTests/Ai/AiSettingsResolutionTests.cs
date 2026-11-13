using Dotto.Ai.Agents;
using Dotto.Ai.Settings;
using Dotto.Application.Configuration;
using Dotto.Common.Configuration;
using Dotto.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Proves that guild- and user-scoped overrides reach the effective AI settings, and that
/// the values used to build the chat client come from those settings.
/// </summary>
public class AiSettingsResolutionTests
{
    private sealed class FakeStore : IConfigurationOverrideStore
    {
        public List<ConfigurationOverride> Rows { get; } = [];

        public string InstanceName => "dotto";

        public Task<IReadOnlyList<ConfigurationOverride>> GetEffectiveAsync(
            ulong? guildId, ulong channelId, ulong userId, CancellationToken ct = default)
        {
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

    private static InteractionConfiguration Build(
        FakeStore store,
        Dictionary<string, string?>? extraConfiguration = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ai:BaseUrl"] = "https://base.example/v1",
            ["Ai:ApiKey"] = "base-key",
            ["Ai:AssistantModel"] = "base-model",
            ["Ai:ChatAssistant:Model"] = "base-assistant-model",
            // The sandbox section exists in appsettings.json; without a key here the
            // section is absent and SandboxOptions cannot bind at all.
            ["Ai:Sandbox:Enabled"] = "true"
        };

        if (extraConfiguration != null)
        {
            foreach (var (key, value) in extraConfiguration)
                settings[key] = value;
        }

        var baseConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var registry = new ConfigurationSchemaRegistry(
        [
            new ConfigurationOptionSchema("Ai", typeof(AiSettings)),
            new ConfigurationOptionSchema("Ai:Sandbox", typeof(SandboxOptions))
        ]);

        return new InteractionConfiguration(baseConfiguration, store, registry);
    }

    private static ConfigurationOverride Row(
        ConfigurationScope scope, ulong target, string key, string value) => new()
    {
        InstanceName = "dotto",
        Scope = scope,
        TargetId = target,
        Key = key,
        Value = value,
        CreatedOn = DateTime.UtcNow,
        UpdatedOn = DateTime.UtcNow
    };

    [Test]
    public async Task GuildOverride_ChangesTheAssistantModelOnlyInThatGuild()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.Guild, 10, "ai:chatassistant:model", "guild-model"));

        var configuration = Build(store);

        var guildSettings = await configuration.GetAsync<AiSettings>(new InteractionScope(10, 20, 30));
        var otherGuildSettings = await configuration.GetAsync<AiSettings>(new InteractionScope(11, 20, 30));

        guildSettings.ChatAssistant.Model.ShouldBe("guild-model");
        otherGuildSettings.ChatAssistant.Model.ShouldBe("base-assistant-model");
    }

    [Test]
    public async Task UserOverride_ChangesTheApiKeyOnlyForThatUser()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.User, 30, "ai:apikey", "user-key"));

        var configuration = Build(store);

        var userSettings = await configuration.GetAsync<AiSettings>(new InteractionScope(10, 20, 30));
        var otherUserSettings = await configuration.GetAsync<AiSettings>(new InteractionScope(10, 20, 31));

        userSettings.ApiKey.ShouldBe("user-key");
        otherUserSettings.ApiKey.ShouldBe("base-key");
    }

    [Test]
    public async Task ChatClientConnection_UsesTheEffectiveModelAndKey()
    {
        var store = new FakeStore();
        store.Rows.Add(Row(ConfigurationScope.Guild, 10, "ai:chatassistant:model", "guild-model"));
        store.Rows.Add(Row(ConfigurationScope.User, 30, "ai:apikey", "user-key"));

        var configuration = Build(store);

        var effective = await configuration.GetAsync<AiSettings>(new InteractionScope(10, 20, 30));
        var connection = AiAgentFactory.ResolveConnection(effective);

        connection.Model.ShouldBe("guild-model");
        connection.ApiKey.ShouldBe("user-key");
        connection.BaseUrl!.ToString().ShouldBe("https://base.example/v1");
    }

    [Test]
    public async Task SandboxIpcBasePath_DefaultsToThePlatformDirectory()
    {
        var configuration = Build(new FakeStore());

        var sandbox = await configuration.GetAsync<SandboxOptions>(new InteractionScope(10, 20, 30));

        sandbox.IpcBasePath.ShouldBe(new SandboxOptions().IpcBasePath);

        if (!OperatingSystem.IsWindows())
            sandbox.IpcBasePath.ShouldBe("/var/lib/dotto-sandbox-ipc");
    }

    [Test]
    public async Task SandboxIpcBasePath_OverrideReachesTheEffectiveOptions()
    {
        var configuration = Build(
            new FakeStore(),
            new Dictionary<string, string?> { ["Ai:Sandbox:IpcBasePath"] = "/tmp/dotto-ipc-override" });

        var sandbox = await configuration.GetAsync<SandboxOptions>(new InteractionScope(10, 20, 30));

        sandbox.IpcBasePath.ShouldBe("/tmp/dotto-ipc-override");
    }
}
