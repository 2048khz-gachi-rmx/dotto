using Dotto.Application.Models;
using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Application.Abstractions.MediaProcessing;

public interface IMediaProcessingService
{
    Task<MediaDownloadResult> ProcessMediaFromUrlAsync(Uri uri, DownloadOptions options, CancellationToken cancellationToken = default);
}