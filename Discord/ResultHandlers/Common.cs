using Dotto.Common.Constants;
using NetCord.Rest;

namespace Dotto.Discord.ResultHandlers;

internal class Common
{
    public static T GetErrorEmbed<T>(string error)
        where T : IMessageProperties, new()
    {
        return new T()
        {
            Embeds =
            [
                new()
                {
                    Color = Constants.Colors.ErrorColor,
                    Fields = [
                        new() { Name = "Command Error", Value = error }
                    ]
                }
            ]
        };
    }
}