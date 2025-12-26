namespace Dotto.Infrastructure.Downloader.Contracts.Models;

public record DownloadOptions
{
    public bool AudioOnly { get; init; }
    
    /// <summary>
    /// Max filesize of a single file
    /// </summary>
    public long MaxFilesize { get; init; } = 1 << 20;

    /// <summary>
    /// Maximum amount of files to download
    /// </summary>
    public long MaxDownloads { get; init; } = 10;
}