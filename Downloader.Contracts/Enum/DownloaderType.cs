namespace Dotto.Infrastructure.Downloader.Contracts.Enum;

public enum DownloaderType
{
    /// <summary>
    /// Use yt-dlp on the local machine to download the media
    /// </summary>
    Ytdl = 1,
    
    /// <summary>
    /// Use <a href="https://github.com/imputnet/cobalt">Cobalt's</a> API to download the media
    /// </summary>
    Cobalt = 2,
}