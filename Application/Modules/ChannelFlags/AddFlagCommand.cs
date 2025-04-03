using System.Text.RegularExpressions;
using Dotto.Application.Entities;
using Dotto.Application.InternalServices.ChannelFlagsService;
using FluentValidation;
using MediatR;
using NetCord;
using NetCord.Rest;

namespace Dotto.Application.Modules.ChannelFlags;

public record AddFlagRequest<T> : IRequest<T>
    where T : IMessageProperties, new()
{
    public ulong ChannelId { get; init; }
    public string FlagName { get; init; } = null!;
}

public class AddFlagMessageRequestHandler<T>(ChannelFlagsService flagsService)
    : IRequestHandler<AddFlagRequest<T>, T>
    where T : IMessageProperties, new()
{
    public async Task<T> Handle(AddFlagRequest<T> request, CancellationToken cancellationToken)
    {
        await flagsService.AddChannelFlag(request.ChannelId, request.FlagName, cancellationToken);
        var newFlags = await flagsService.GetChannelFlags(request.ChannelId, cancellationToken)
            ?? ["WTF"];

        var newFlagsStr = string.Join("; ", newFlags.Select(f => Format.SmallCodeBlock(f)));
        
        var msg = new T();
        msg.WithContent("Flag added! New flags:\n" + newFlagsStr);
        
        return msg;
    }
}

public class AddFlagRequestValidator<T> : AbstractValidator<AddFlagRequest<T>>
    where T : IMessageProperties, new()
{
    public AddFlagRequestValidator()
    {
        RuleFor(c => c.FlagName)
            .MaximumLength(Constants.ChannelFlags.MaxLength)
            .WithMessage("Flag name exceeds maximum length ({TotalLength} > {MaxLength})");

        RuleFor(c => c.FlagName)
            .Matches(@"^[a-z0-9_]+$", RegexOptions.IgnoreCase)
            .WithMessage("Flag contains invalid characters");
    }
}