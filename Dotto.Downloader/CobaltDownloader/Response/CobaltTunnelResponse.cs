namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

internal class CobaltTunnelResponse : CobaltGenericResponse
{
    public string Url { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
}