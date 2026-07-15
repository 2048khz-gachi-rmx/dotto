namespace Dotto.Infrastructure.Downloader.Settings;

public class DownloaderSettings
{
    /// <remarks>C# defaults null string in configs to empty strings instead</remarks>
    public string? TempPath { get; init; }
    
    public CobaltSettings? Cobalt { get; init; }

    /// <remarks>Optional path to a Netscape-format cookies file passed to yt-dlp via --cookies. Null = no cookies.</remarks>
    public string? CookieFile { get; init; }
}
