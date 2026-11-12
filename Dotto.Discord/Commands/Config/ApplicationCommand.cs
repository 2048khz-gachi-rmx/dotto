using Dotto.Discord.CommandHandlers.Config;
using Dotto.Discord.Models.Config;
using Dotto.Domain.Entities;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Commands.Config;

[SlashCommand("config", "Manage runtime configuration overrides")]
public class ApplicationCommand(ConfigCommandHandler handler)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("set", "Set a configuration override")]
    public async Task Set(
        [SlashCommandParameter(
            Name = "key",
            Description = "Configuration key, e.g. Ai:ChatAssistant:Model",
            MaxLength = 200,
            AutocompleteProviderType = typeof(ConfigKeyAutocompleteProvider))]
        string key,
        [SlashCommandParameter(Name = "value", Description = "Value to store", MaxLength = 4000)]
        string value,
        [SlashCommandParameter(Name = "scope", Description = "Scope the override applies to")]
        ConfigScopeOption scope = ConfigScopeOption.Global,
        [SlashCommandParameter(Name = "user", Description = "User to target (scope=user)")]
        User? user = null)
    {
        var result = await handler.SetAsync<InteractionMessageProperties>(
            Context.User.Id,
            Context.Guild?.Id,
            Context.Channel.Id,
            ToScope(scope),
            user?.Id,
            key,
            value);

        await Respond(result);
    }

    [SubSlashCommand("unset", "Delete a configuration override")]
    public async Task Unset(
        [SlashCommandParameter(
            Name = "key",
            Description = "Configuration key",
            MaxLength = 200,
            AutocompleteProviderType = typeof(ConfigKeyAutocompleteProvider))]
        string key,
        [SlashCommandParameter(Name = "scope", Description = "Scope the override applies to")]
        ConfigScopeOption scope = ConfigScopeOption.Global,
        [SlashCommandParameter(Name = "user", Description = "User to target (scope=user)")]
        User? user = null)
    {
        var result = await handler.UnsetAsync<InteractionMessageProperties>(
            Context.User.Id,
            Context.Guild?.Id,
            Context.Channel.Id,
            ToScope(scope),
            user?.Id,
            key);

        await Respond(result);
    }

    [SubSlashCommand("get", "Show a stored configuration override")]
    public async Task Get(
        [SlashCommandParameter(
            Name = "key",
            Description = "Configuration key",
            MaxLength = 200,
            AutocompleteProviderType = typeof(ConfigKeyAutocompleteProvider))]
        string key,
        [SlashCommandParameter(Name = "scope", Description = "Scope the override applies to")]
        ConfigScopeOption scope = ConfigScopeOption.Global,
        [SlashCommandParameter(Name = "user", Description = "User to target (scope=user)")]
        User? user = null)
    {
        var result = await handler.GetAsync<InteractionMessageProperties>(
            Context.User.Id,
            Context.Guild?.Id,
            Context.Channel.Id,
            ToScope(scope),
            user?.Id,
            key);

        await Respond(result);
    }

    [SubSlashCommand("list", "List stored configuration overrides")]
    public async Task List(
        [SlashCommandParameter(Name = "scope", Description = "Scope to list")]
        ConfigScopeOption scope = ConfigScopeOption.Global,
        [SlashCommandParameter(Name = "user", Description = "User to list (scope=user)")]
        User? user = null)
    {
        var result = await handler.ListAsync<InteractionMessageProperties>(
            Context.User.Id,
            Context.Guild?.Id,
            Context.Channel.Id,
            ToScope(scope),
            user?.Id);

        await Respond(result);
    }

    private Task Respond(InteractionMessageProperties result)
    {
        result.Flags = MessageFlags.Ephemeral;
        return RespondAsync(InteractionCallback.Message(result));
    }

    private static ConfigurationScope ToScope(ConfigScopeOption scope) => (ConfigurationScope)(int)scope;

    public class ConfigKeyAutocompleteProvider(ConfigCommandHandler handler)
        : IAutocompleteProvider<AutocompleteInteractionContext>
    {
        public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
            ApplicationCommandInteractionDataOption option,
            AutocompleteInteractionContext context)
        {
            return ValueTask.FromResult<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(
                GetChoices(option.Value ?? string.Empty, ResolveRequestedScope(context)));
        }

        internal IEnumerable<ApplicationCommandOptionChoiceProperties> GetChoices(string input)
            => GetChoices(input, ConfigurationScope.Global);

        internal IEnumerable<ApplicationCommandOptionChoiceProperties> GetChoices(
            string input, ConfigurationScope scope)
        {
            return handler.GetKnownKeys(scope)
                .Where(key => key.Contains(input, StringComparison.OrdinalIgnoreCase))
                .Take(25)
                .Select(key => new ApplicationCommandOptionChoiceProperties(key, key));
        }

        /// <summary>
        /// The `scope` option may not be filled yet while `key` is being completed; the default
        /// matches the command's own default.
        /// </summary>
        private static ConfigurationScope ResolveRequestedScope(AutocompleteInteractionContext context)
        {
            var value = context.Interaction.Data.Options
                .FirstOrDefault(option => option.Name.Equals("scope", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return value?.ToLowerInvariant() switch
            {
                "guild" => ConfigurationScope.Guild,
                "channel" => ConfigurationScope.Channel,
                "user" => ConfigurationScope.User,
                _ => ConfigurationScope.Global
            };
        }
    }
}
