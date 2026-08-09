using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Infrastructure.Downloader.Contracts.Abstractions;

public interface IDownloaderService
{
    /// <exception cref="ApplicationException">yt-dlp exits with a failure code</exception>
    public Task<IList<DownloadedMedia>> Download(string query, DownloadOptions options, CancellationToken cancellationToken = default);
}