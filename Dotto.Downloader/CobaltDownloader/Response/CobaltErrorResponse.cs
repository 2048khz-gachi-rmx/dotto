namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

// why the fuck are these nested
internal class CobaltErrorResponse : CobaltGenericResponse
{
    public CobaltError Error { get; set; } = new();
}

internal class CobaltError
{
    public string Code { get; set; } = string.Empty;
    public CobaltErrorContext? Context { get; set; }
}

internal class CobaltErrorContext
{
    public string? Service { get; set; }
    public int? Limit { get; set; }
}