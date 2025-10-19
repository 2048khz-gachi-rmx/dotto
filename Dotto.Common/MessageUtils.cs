using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Common;

public static class MessageUtils
{
    public static Task<RestMessage> SuppressEmbeds(this Message message)
    {
        return message.ModifyAsync(opt => opt.WithFlags(MessageFlags.SuppressEmbeds));
    }
}