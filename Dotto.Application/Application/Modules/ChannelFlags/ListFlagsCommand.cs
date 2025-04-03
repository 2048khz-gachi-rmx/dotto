using Dotto.Application.InternalServices.ChannelFlagsService;
using Dotto.Common;
using MediatR;
using NetCord;
using NetCord.Rest;

namespace Dotto.Application.Modules.ChannelFlags;

public record ListFlagsRequest<T> : IRequest<T>
    where T : IMessageProperties, new()
{
    public ulong ChannelId { get; init; }
}

public class ListFlagsHandler<T>(ChannelFlagsService flagsService)
    : IRequestHandler<ListFlagsRequest<T>, T>
    where T : IMessageProperties, new()
{
    public async Task<T> Handle(ListFlagsRequest<T> request, CancellationToken cancellationToken)
    {
        var flags = await flagsService.GetChannelFlags(request.ChannelId, cancellationToken);

        var msg = new T();

        if (flags.IsNullOrEmpty())
        {
            msg.WithContent("Channel has no flags.");
            return msg;
        }
        
        var flagsStr = string.Join("; ", flags.Select(f => Format.SmallCodeBlock(f)));
        msg.WithContent("Channel has the following flags:\n" + flagsStr);
        
        return msg;
    }
}