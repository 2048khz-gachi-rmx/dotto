namespace Dotto.Application.Entities;

public class DownloadedMediaRecord
{
    /// <summary>
    /// Auto-incrementing ID
    /// </summary>
    public required int Id { get; init; }
    
    /// <summary>
    /// URL from which this media was downloaded
    /// </summary>
    public required string DownloadedFrom { get; init; }
    
    /// <summary>
    /// URL to the uploaded video (external storage or Discord)
    /// </summary>
    public required string MediaUrl { get; init; }

    /// <summary>
    /// ID of the channel in which this media was posted
    /// </summary>
    public required ulong ChannelId { get; init; }
    
    /// <summary>
    /// ID of the message in which this media was embedded
    /// </summary>
    public required ulong MessageId { get; init; }
    
    /// <summary>
    /// ID of the user that invoked this media's download
    /// </summary>
    public required ulong InvokerId { get; init; }
    
    /// <summary>
    /// When this media was downloaded
    /// </summary>
    public required DateTime CreatedOn { get; init; }
}