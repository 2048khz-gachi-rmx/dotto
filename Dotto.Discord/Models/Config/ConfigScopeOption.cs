using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Models.Config;

/// <summary>
/// Slash/text command representation of an override scope, with lowercase choices.
/// </summary>
public enum ConfigScopeOption
{
    [SlashCommandChoice(Name = "global")]
    Global = 0,

    [SlashCommandChoice(Name = "guild")]
    Guild = 1,

    [SlashCommandChoice(Name = "channel")]
    Channel = 2,

    [SlashCommandChoice(Name = "user")]
    User = 3
}
