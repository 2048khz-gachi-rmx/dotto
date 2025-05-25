using NetCord.Rest;

namespace Dotto.Application.Modules.Download;

public class DownloadedMediaMessage<T>
    where T : IMessageProperties
{
    /// <summary>
    /// Generated message to respond with
    /// </summary>
    public required T Message { get; init; }

    /// <summary>
    /// What URL these medias were downloaded from
    /// </summary>
    public required Uri SourceUrl { get; init; }
    
    /// <summary>
    /// Videos uploaded to external storage
    /// </summary>
    public List<Uri> ExternalVideos { get; init; } = new();
    
    /// <summary>
    /// Videos uploaded to Discord
    /// </summary>
    public List<AttachmentProperties> AttachedVideos { get; init; } = new();
}