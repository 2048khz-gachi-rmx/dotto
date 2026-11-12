using Dotto.Application.Configuration;
using Dotto.Discord.Services;
using Dotto.Domain.Entities;
using NetCord.Rest;

namespace Dotto.Discord.CommandHandlers.Config;

public class ConfigCommandHandler(
    IBotOwnerService ownerService,
    IConfigurationOverrideStore overrideStore,
    IConfigurationValueValidator valueValidator,
    IInteractionConfiguration interactionConfiguration,
    ConfigurationSchemaRegistry schemaRegistry)
{
    private const string NotOwnerMessage = "Only the bot owner can manage configuration.";
    private const int MaxMessageLength = 1900;

    public async Task<T> SetAsync<T>(
        ulong actorId,
        ulong? guildId,
        ulong channelId,
        ConfigurationScope scope,
        ulong? targetUserId,
        string key,
        string value,
        CancellationToken ct = default)
        where T : IMessageProperties, new()
    {
        if (!await ownerService.IsOwnerAsync(actorId, ct))
            return Error<T>(NotOwnerMessage);

        if (!TryResolveTarget(scope, guildId, channelId, actorId, targetUserId, out var target, out var error))
            return Error<T>(error);

        var baseConfiguration = await interactionConfiguration.BuildAsync(
            new InteractionScope(guildId == 0 ? null : guildId, channelId, actorId), ct);

        var validation = valueValidator.Validate(key, value, target.Scope, baseConfiguration);
        if (!validation.IsValid)
            return Error<T>(validation.Error ?? "Invalid configuration value.");

        await overrideStore.SetAsync(target, key, value, ct);

        var content = $"Set `{key}` for {Describe(target)}.";
        if (validation.Warning != null)
            content += $"\nWarning: {validation.Warning}";

        return Message<T>(content);
    }

    public async Task<T> UnsetAsync<T>(
        ulong actorId,
        ulong? guildId,
        ulong channelId,
        ConfigurationScope scope,
        ulong? targetUserId,
        string key,
        CancellationToken ct = default)
        where T : IMessageProperties, new()
    {
        if (!await ownerService.IsOwnerAsync(actorId, ct))
            return Error<T>(NotOwnerMessage);

        if (!TryResolveTarget(scope, guildId, channelId, actorId, targetUserId, out var target, out var error))
            return Error<T>(error);

        var deleted = await overrideStore.DeleteAsync(target, key, ct);

        return Message<T>(deleted
            ? $"Unset `{key}` for {Describe(target)}."
            : $"`{key}` was not set for {Describe(target)}.");
    }

    public async Task<T> GetAsync<T>(
        ulong actorId,
        ulong? guildId,
        ulong channelId,
        ConfigurationScope scope,
        ulong? targetUserId,
        string key,
        CancellationToken ct = default)
        where T : IMessageProperties, new()
    {
        if (!await ownerService.IsOwnerAsync(actorId, ct))
            return Error<T>(NotOwnerMessage);

        if (!TryResolveTarget(scope, guildId, channelId, actorId, targetUserId, out var target, out var error))
            return Error<T>(error);

        var row = await overrideStore.GetAsync(target, key, ct);
        if (row == null)
            return Message<T>($"`{key}` is not set for {Describe(target)}.");

        var value = ConfigurationSecretMask.MaskIfSecret(row.Key, row.Value);
        return Message<T>($"`{row.Key}` = `{value}` ({Describe(target)})");
    }

    public async Task<T> ListAsync<T>(
        ulong actorId,
        ulong? guildId,
        ulong channelId,
        ConfigurationScope scope,
        ulong? targetUserId,
        CancellationToken ct = default)
        where T : IMessageProperties, new()
    {
        if (!await ownerService.IsOwnerAsync(actorId, ct))
            return Error<T>(NotOwnerMessage);

        if (!TryResolveTarget(scope, guildId, channelId, actorId, targetUserId, out var target, out var error))
            return Error<T>(error);

        var rows = await overrideStore.ListAsync(target, ct);
        if (rows.Count == 0)
            return Message<T>($"No overrides are set for {Describe(target)}.");

        var lines = rows.Select(row =>
            $"`{row.Key}` = `{ConfigurationSecretMask.MaskIfSecret(row.Key, row.Value)}`");

        return Message<T>(Truncate($"Overrides for {Describe(target)}:\n" + string.Join('\n', lines)));
    }

    /// <summary>
    /// Known keys feed slash-command autocomplete. Global accepts every overridable key; scoped
    /// targets only accept keys that are resolved per interaction.
    /// </summary>
    public IReadOnlyList<string> GetKnownKeys(ConfigurationScope scope = ConfigurationScope.Global)
        => schemaRegistry.GetKnownKeys(scope);

    private static bool TryResolveTarget(
        ConfigurationScope scope,
        ulong? guildId,
        ulong channelId,
        ulong actorId,
        ulong? targetUserId,
        out ConfigurationTarget target,
        out string error)
    {
        target = ConfigurationTarget.Global;
        error = string.Empty;

        switch (scope)
        {
            case ConfigurationScope.Global:
                return true;

            case ConfigurationScope.Guild when guildId is null or 0:
                error = "Guild scope needs a guild; run this in a server.";
                return false;

            case ConfigurationScope.Guild:
                target = ConfigurationTarget.Guild(guildId.Value);
                return true;

            case ConfigurationScope.Channel when channelId == 0:
                error = "Channel scope needs a channel.";
                return false;

            case ConfigurationScope.Channel:
                target = ConfigurationTarget.Channel(channelId);
                return true;

            case ConfigurationScope.User:
                var userId = targetUserId ?? actorId;
                if (userId == 0)
                {
                    error = "User scope needs a user.";
                    return false;
                }

                target = ConfigurationTarget.User(userId);
                return true;

            default:
                error = $"Unknown scope '{scope}'.";
                return false;
        }
    }

    private static string Describe(ConfigurationTarget target) => target.Scope switch
    {
        ConfigurationScope.Global => "global",
        ConfigurationScope.Guild => $"guild {target.TargetId}",
        ConfigurationScope.Channel => $"channel {target.TargetId}",
        ConfigurationScope.User => $"user {target.TargetId}",
        _ => $"scope {target.Scope} {target.TargetId}"
    };

    private static string Truncate(string content) =>
        content.Length <= MaxMessageLength
            ? content
            : content[..MaxMessageLength] + "\n…";

    private static T Message<T>(string content) where T : IMessageProperties, new()
        => new() { Content = content };

    private static T Error<T>(string content) where T : IMessageProperties, new()
        => new() { Content = $"⚠️ {content}" };
}
