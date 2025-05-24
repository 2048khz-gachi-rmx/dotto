using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace Dotto.Discord.EventHandlers;

[GatewayEvent(nameof(GatewayClient.MessageCreate))]

public class MessageUrlDownload : IGatewayEventHandler<Message>
{
    public ValueTask HandleAsync(Message arg)
    {
        throw new NotImplementedException();
    }
}