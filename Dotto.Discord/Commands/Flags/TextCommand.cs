using Dotto.Discord.CommandHandlers.Flags;
using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace Dotto.Discord.Commands.Flags;

[UsedImplicitly]
public class TextCommand(IFlagCommandHandler flagHandler) : CommandModule<CommandContext>
{
    [UsedImplicitly]
    [Command("addflag")]
    public async Task AddFlag(string flagName)
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;

        var result = await flagHandler.AddFlag<ReplyMessageProperties>(textGuildChannel.Id, flagName);

        await ReplyAsync(result);
    }
    
    [UsedImplicitly]
    [Command("removeflag")]
    public async Task RemoveFlag(string flagName)
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;

        var result = await flagHandler.RemoveFlag<ReplyMessageProperties>(textGuildChannel.Id, flagName);

        await ReplyAsync(result);
    }

    [UsedImplicitly]
    [Command("listflags")]
    public async Task ListFlags()
    {
        var textGuildChannel = await CheckChannelIsGuild();
        if (textGuildChannel == null) return;

        var result = await flagHandler.ListFlags<ReplyMessageProperties>(textGuildChannel.Id);

        await ReplyAsync(result);
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