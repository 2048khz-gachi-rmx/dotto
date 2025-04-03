using Dotto.Application.Entities;
using Dotto.Application.Modules.ChannelFlags;
using MediatR;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Commands.Flags;

public class SlashCommand(IMediator mediator) : ApplicationCommandModule<ApplicationCommandContext>
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
        
        var message = await mediator.Send(new AddFlagRequest<InteractionMessageProperties>()
        {
            ChannelId = textGuildChannel.Id,
            FlagName = flagName
        });
        
        await RespondAsync(InteractionCallback.Message(message));
    }
    
    [SlashCommand("removeflag", "Removes a flag to this channel")]
    public async Task RemoveFlag(
        [SlashCommandParameter(
            MaxLength = Constants.ChannelFlags.MaxLength)]
        string flagName)
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;
        
        var message = await mediator.Send(new RemoveFlagRequest<InteractionMessageProperties>()
        {
            ChannelId = textGuildChannel.Id,
            FlagName = flagName
        });
        
        await RespondAsync(InteractionCallback.Message(message));
    }
    
    [SlashCommand("listflags", "Lists this channel's flags")]
    public async Task ListFlags()
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;
        
        var message = await mediator.Send(new ListFlagsRequest<InteractionMessageProperties>()
        {
            ChannelId = textGuildChannel.Id,
        });
        
        await RespondAsync(InteractionCallback.Message(message));
    }

    private async Task<TextGuildChannel?> CheckChannelIsGuild()
    {
        if (Context.Channel is not TextGuildChannel textGuildChannel)
        {
            var response = new InteractionMessageProperties()
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