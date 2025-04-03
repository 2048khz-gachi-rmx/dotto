using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace Dotto.Commands.Download;

/// <summary>
/// Listens to the MessageCreate event, tries matching whitelisted URL's
/// and attempts downloading then reembedding the video from within them.
/// </summary>
[GatewayEvent(nameof(GatewayClient.MessageCreate))]
public class GatewayListener : IGatewayEventHandler<Message>
{
    public ValueTask HandleAsync(Message arg)
    {
        if (arg.Author.IsBot) return default;
        
        throw new NotImplementedException();
        return default;
    }
}