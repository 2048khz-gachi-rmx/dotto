namespace Dotto.Application.Entities;

public class DownloadedMediaRecord
{
    /// <summary>
    /// Auto-incrementing ID
    /// </summary>
    public int Id { get; init; }
    
    /// <summary>
    /// URL from which this media was downloaded
    /// </summary>
    public string DownloadedFrom { get; init; } = null!;
    
    /// <summary>
    /// URL to the uploaded video (external storage or Discord)
    /// </summary>
    public string MediaUrl { get; init; }

    /// <summary>
    /// ID of the channel in which this media was posted
    /// </summary>
    public ulong ChannelId { get; init; }
    
    /// <summary>
    /// ID of the message in which this media was embedded
    /// </summary>
    public ulong MessageId { get; init; }
    
    /// <summary>
    /// ID of the user that invoked this media's download
    /// </summary>
    public ulong InvokerId { get; init; }
    
    /// <summary>
    /// When this media was downloaded
    /// </summary>
    public DateTime CreatedOn { get; init; }
}