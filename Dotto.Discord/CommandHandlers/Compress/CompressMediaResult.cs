using Dotto.Common;
using NetCord.Rest;

namespace Dotto.Discord.CommandHandlers.Compress;

public class CompressMediaResult<T>
    where T : IMessageProperties
{
    public required T Message { get; init; }
    
    public List<AttachmentProperties> AttachedVideos { get; init; } = [];
    
    public bool HasAnyMedia => !AttachedVideos.IsEmpty();
}
