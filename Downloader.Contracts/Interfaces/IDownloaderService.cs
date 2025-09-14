using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Infrastructure.Downloader.Contracts.Interfaces;

public interface IDownloaderService
{
    /// <exception cref="IndexOutOfRangeException">Downloaded video was bigger than uploadLimit</exception>
    /// <exception cref="ApplicationException">yt-dlp exits with a failure code</exception>
    public Task<IList<DownloadedMedia>> Download(Uri uri, DownloadOptions options, CancellationToken ct = default);
}

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