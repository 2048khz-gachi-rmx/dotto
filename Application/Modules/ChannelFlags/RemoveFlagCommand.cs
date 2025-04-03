using Dotto.Application.InternalServices.ChannelFlagsService;
using MediatR;
using NetCord;
using NetCord.Rest;

namespace Dotto.Application.Modules.ChannelFlags;

public record RemoveFlagRequest<T> : IRequest<T>
    where T : IMessageProperties, new()
{
    public ulong ChannelId { get; init; }
    public string FlagName { get; init; } = null!;
}

public class RemoveFlagRequestHandler<T>(ChannelFlagsService flagsService)
    : IRequestHandler<RemoveFlagRequest<T>, T>
    where T : IMessageProperties, new()
{
    public async Task<T> Handle(RemoveFlagRequest<T> request, CancellationToken cancellationToken)
    {
        var ok = await flagsService.RemoveChannelFlag(request.ChannelId, request.FlagName, cancellationToken);

        var msg = new T();
        
        if (ok)
        {
            var newFlags = await flagsService.GetChannelFlags(request.ChannelId, cancellationToken);
            var newFlagsStr = string.Join("; ", newFlags.Select(f => Format.SmallCodeBlock(f)));
            msg.WithContent("Flag removed! New flags:\n" + newFlagsStr);
        }
        else
        {
            msg.WithContent($"Channel didn't have flag \"{request.FlagName}\".");
        }

        return msg;
    }
}