using Dotto.Common;
using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Application.Models;

public class MediaDownloadResult
{
    public IList<DownloadedMedia> Media { get; set; } = [];
    public IList<MediaDownloadError> Errors { get; set; } = [];
    
    public bool IsSuccess => !Media.IsNullOrEmpty();
}