namespace Dotto.Application.Models;

public enum MediaErrorCode
{
    /// <summary>
    /// Intended for when the downloader service is unavailable (failed HTTP responses, etc...)
    /// </summary>
    ServiceUnavailable,
    
    /// <summary>
    /// Intended for anticipated errors, thrown by us explicitly (non-zero exit codes, unexpected API responses, etc...)
    /// </summary>
    DownloaderError,
}

public record MediaDownloadError
{
    public required MediaErrorCode ErrorCode { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
}