namespace Services.DownloaderService;

public interface IDownloaderService
{
    /// <param name="uri"></param>
    /// <param name="audioOnly"></param>
    /// <param name="uploadLimit">TODO: this needs to actually play into the filters...</param>
    /// <param name="ct"></param>
    /// <exception cref="IndexOutOfRangeException">Downloaded video was bigger than uploadLimit</exception>
    /// <exception cref="ApplicationException">yt-dlp exits with a failure code</exception>
    public Task<DownloadedMedia> Download(Uri uri, bool audioOnly, long uploadLimit = long.MaxValue, CancellationToken ct = default);
}