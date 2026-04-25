using System.Text.RegularExpressions;
using Dotto.Application.InternalServices;
using Dotto.Common;
using Dotto.Common.Constants;
using NetCord;
using NetCord.Rest;

namespace Dotto.Discord.CommandHandlers.Flags;

public interface IFlagCommandHandler
{
    Task<T> AddFlag<T>(ulong channelId, string flagName, CancellationToken ct = default)
        where T : IMessageProperties, new();

    Task<T> RemoveFlag<T>(ulong channelId, string flagName, CancellationToken ct = default)
        where T : IMessageProperties, new();

    Task<T> ListFlags<T>(ulong channelId, CancellationToken ct = default)
        where T : IMessageProperties, new();
}

internal class FlagCommandHandler(
    IChannelFlagsService flagsService) : IFlagCommandHandler
{
    public async Task<T> AddFlag<T>(ulong channelId, string flagName, CancellationToken ct = default)
        where T : IMessageProperties, new()
    {
        if (flagName.Length > Constants.ChannelFlags.MaxLength)
            throw new ArgumentException($"Flag name exceeds maximum length ({flagName.Length} > {Constants.ChannelFlags.MaxLength}).");

        if (!Regex.IsMatch(flagName, "^[a-z0-9_]+$"))
            throw new ArgumentException("Flag contains invalid characters. Use only lowercase letters, numbers, and underscores.");

        await flagsService.AddChannelFlag(channelId, flagName, ct);

        var newFlags = await flagsService.GetChannelFlags(channelId, ct);
        var newFlagsStr = string.Join("; ", newFlags.Select(f => Format.SmallCodeBlock(f)));

        var msg = new T
        {
            Content = "Flag added! New flags:\n" + newFlagsStr
        };

        return msg;
    }

    public async Task<T> RemoveFlag<T>(ulong channelId, string flagName, CancellationToken ct = default)
        where T : IMessageProperties, new()
    {
        var ok = await flagsService.RemoveChannelFlag(channelId, flagName, ct);

        var msg = new T();

        if (ok)
        {
            var newFlags = await flagsService.GetChannelFlags(channelId, ct);
            var newFlagsStr = string.Join("; ", newFlags.Select(f => Format.SmallCodeBlock(f)));
            msg.Content = "Flag removed! New flags:\n" + newFlagsStr;
        }
        else
        {
            msg.Content = $"Channel didn't have flag \"{flagName}\".";
        }

        return msg;
    }

    public async Task<T> ListFlags<T>(ulong channelId, CancellationToken ct = default)
        where T : IMessageProperties, new()
    {
        var flags = await flagsService.GetChannelFlags(channelId, ct);

        var msg = new T();

        if (flags.IsEmpty())
        {
            msg.Content = "Channel has no flags.";
            return msg;
        }

        var flagsStr = string.Join("; ", flags.Select(f => Format.SmallCodeBlock(f)));
        msg.Content = "Channel has the following flags:\n" + flagsStr;

        return msg;
    }
}
