using Dotto.Common.Constants;

namespace Dotto.Application.Entities;

public record ChannelFlags
{
    public ulong ChannelId { get; init; }

    public IList<string> Flags { get; init; } = [];
    
    public DateTime UpdatedOn { get; set; }

    protected ChannelFlags() { /* EF */ }

    public ChannelFlags(ulong channelId)
    {
        ChannelId = channelId;
    }
    
    /// <returns>Whether the flag was actually added. If the flag was already present, returns false</returns>
    public bool AddFlag(string flag, DateTime when)
    {
        if (flag.Length > Constants.ChannelFlags.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(flag),
                $"Flag name is longer than max allowed (>{Constants.ChannelFlags.MaxLength}).");

        if (Flags.Count >= Constants.ChannelFlags.MaxFlagsInChannel)
            throw new ArgumentOutOfRangeException(nameof(flag),
                $"Channel reached the flag limit ({Constants.ChannelFlags.MaxFlagsInChannel}).");
        
        if (Flags.Contains(flag)) return false;

        Flags.Add(flag);
        UpdatedOn = when;

        return true;
    }
    
    /// <returns>Whether the flag was actually removed. If the flag wasn't present, returns false</returns>
    public bool RemoveFlag(string flag, DateTime when)
    {
        if (!Flags.Remove(flag)) return false;
        
        UpdatedOn = when;
        return true;
    }
}