using Dotto.Common.Constants;
using Dotto.Discord.CommandHandlers.Flags;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Commands.Flags;

public class SlashCommand(IFlagCommandHandler flagHandler) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("addflag", "Adds a flag to this channel")]
    public async Task AddFlag(
        [SlashCommandParameter(
            MaxLength = Constants.ChannelFlags.MaxLength,
            AutocompleteProviderType = typeof(FunctionalFlagAutocompleteProvider))]
        string flagName)
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;

        var result = await flagHandler.AddFlag<InteractionMessageProperties>(textGuildChannel.Id, flagName);

        await RespondAsync(InteractionCallback.Message(result));
    }
    
    [SlashCommand("removeflag", "Removes a flag to this channel")]
    public async Task RemoveFlag(
        [SlashCommandParameter(
            MaxLength = Constants.ChannelFlags.MaxLength)]
        string flagName)
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;

        var result = await flagHandler.RemoveFlag<InteractionMessageProperties>(textGuildChannel.Id, flagName);

        await RespondAsync(InteractionCallback.Message(result));
    }

    [SlashCommand("listflags", "Lists this channel's flags")]
    public async Task ListFlags()
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;

        var result = await flagHandler.ListFlags<InteractionMessageProperties>(textGuildChannel.Id);

        await RespondAsync(InteractionCallback.Message(result));
    }

    private async Task<TextGuildChannel?> CheckChannelIsGuild()
    {
        if (Context.Channel is not TextGuildChannel textGuildChannel)
        {
            var response = new InteractionMessageProperties
            {
                Flags = MessageFlags.Ephemeral,
                Content = "Can't set flags on a non-guild text channel."
            };

            await RespondAsync(InteractionCallback.Message(response));
            return null;
        }

        return textGuildChannel;
    }

    public class FunctionalFlagAutocompleteProvider : IAutocompleteProvider<AutocompleteInteractionContext>
    {
        public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
        {
            return ValueTask.FromResult(Constants.ChannelFlags.FunctionalFlagsList
                .Select(f => new ApplicationCommandOptionChoiceProperties(f, f)))!;
        }
    }
}