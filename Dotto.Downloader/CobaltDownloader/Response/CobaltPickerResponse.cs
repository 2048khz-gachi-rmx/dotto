namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

internal class CobaltPickerResponse : CobaltGenericResponse
{
    public string? Audio { get; init; }
    public string? AudioFilename { get; init; }
    public CobaltPickerItem[] Picker { get; init; } = [];
}

internal class CobaltPickerItem
{
    public string Type { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? Thumb { get; init; }
}