namespace Dotto.Infrastructure.Downloader.Settings;

public class DownloaderSettings
{
    /// <remarks>C# defaults null string in configs to empty strings instead</remarks>
    public string? TempPath { get; init; }
}