using Dotto.Application.Abstractions.Factories;
using Dotto.Application.Abstractions.MediaProcessing;
using Dotto.Application.Models;
using Dotto.Common;
using Dotto.Common.Exceptions;
using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Application.InternalServices.MediaProcessing;

public class MediaProcessingService(IUrlCorrector urlCorrector,
    IDownloaderServiceFactory downloaderFactory)
    : IMediaProcessingService
{
    public async Task<MediaDownloadResult> ProcessMediaFromUrlAsync(Uri uri, DownloadOptions options, CancellationToken cancellationToken = default)
    {
        // if there are any replacements to be done on the URL, do them
        var fixedUrl = urlCorrector.CorrectUrl(uri);
        var downloaders = downloaderFactory.CreateDownloaderService(fixedUrl);
        var response = new MediaDownloadResult();

        foreach (var downloader in downloaders)
        {
            try
            {
                var videos = await downloader.Download(fixedUrl, options, cancellationToken);

                if (videos.IsNullOrEmpty())
                    continue;
                
                response.Media = videos;
                break;
            }
            catch (ServiceUnavailableException ex)
            {
                response.Errors.Add(new MediaDownloadError()
                {
                    ErrorCode = MediaErrorCode.ServiceUnavailable,
                    Message = $"Downloader service \"{ex.ServiceName}\" was unavailable..."
                });
            }
            catch (ApplicationException ex)
            {
                var innerMessage = ex.InnerException?.Message;
                if (innerMessage.IsNullOrWhitespace())
                {
                    innerMessage = "[none provided]";

                    if (ex.InnerException?.StackTrace != null)
                        innerMessage += "\n" + ex.InnerException?.StackTrace;
                }
                
                response.Errors.Add(new MediaDownloadError()
                {
                    ErrorCode = MediaErrorCode.DownloaderError,
                    Message = ex.Message,
                    Details = innerMessage
                });
            }
        }

        return response;
    }
}