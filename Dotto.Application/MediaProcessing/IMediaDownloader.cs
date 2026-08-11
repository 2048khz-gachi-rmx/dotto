using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Application.MediaProcessing;

public interface IMediaDownloader
{
    Task<MediaDownloadResult> DownloadMediaFromUrl(Uri uri, DownloadOptions options, CancellationToken cancellationToken = default);
}