namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

internal class CobaltPickerResponse : CobaltGenericResponse
{
    public string? Audio { get; set; }
    public string? AudioFilename { get; set; }
    public CobaltPickerItem[] Picker { get; set; } = Array.Empty<CobaltPickerItem>();
}

internal class CobaltPickerItem
{
    public string Type { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Thumb { get; set; }
}