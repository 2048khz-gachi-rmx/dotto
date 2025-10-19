using Dotto.Application.Modules.ChannelFlags;
using JetBrains.Annotations;
using MediatR;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace Dotto.Discord.Commands.Flags;

[UsedImplicitly]
public class TextCommand(IMediator mediator) : CommandModule<CommandContext>
{
    [UsedImplicitly]
    [Command("addflag")]
    public async Task AddFlag(string flagName)
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;
        
        var message = await mediator.Send(new AddFlagRequest<ReplyMessageProperties>
        {
            ChannelId = textGuildChannel.Id,
            FlagName = flagName
        });
        
        await ReplyAsync(message);
    }
    
    [UsedImplicitly]
    [Command("removeflag")]
    public async Task RemoveFlag(string flagName)
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;
        
        var message = await mediator.Send(new RemoveFlagRequest<ReplyMessageProperties>
        {
            ChannelId = textGuildChannel.Id,
            FlagName = flagName
        });
        
        await ReplyAsync(message);
    }
    
    [UsedImplicitly]
    [Command("listflags")]
    public async Task ListFlags()
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;
        
        var message = await mediator.Send(new ListFlagsRequest<ReplyMessageProperties>
        {
            ChannelId = textGuildChannel.Id,
        });
        
        await ReplyAsync(message);
    }
    
    private async Task<TextGuildChannel?> CheckChannelIsGuild()
    {
        if (Context.Channel is not TextGuildChannel textGuildChannel)
        {
            var response = new ReplyMessageProperties
            {
                Flags = MessageFlags.Ephemeral,
                Content = "Can't set flags on a non-guild text channel."
            };

            await ReplyAsync(response);
            return null;
        }

        return textGuildChannel;
    }
}