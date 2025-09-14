namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

internal class CobaltLocalProcessingResponse : CobaltGenericResponse
{
    public string Type { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string[] Tunnel { get; set; } = Array.Empty<string>();
    public CobaltOutput Output { get; set; } = new();
    public CobaltAudio? Audio { get; set; }
    public bool? IsHLS { get; set; }
}

internal class CobaltOutput
{
    public string Type { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public CobaltMetadata? Metadata { get; set; }
    public bool Subtitles { get; set; }
}

internal class CobaltMetadata
{
    public string? Album { get; set; }
    public string? Composer { get; set; }
    public string? Genre { get; set; }
    public string? Copyright { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Track { get; set; }
    public string? Date { get; set; }
    public string? Sublanguage { get; set; }
}

internal class CobaltAudio
{
    public bool Copy { get; set; }
    public string Format { get; set; } = string.Empty;
    public string Bitrate { get; set; } = string.Empty;
    public bool? Cover { get; set; }
    public bool? CropCover { get; set; }
}