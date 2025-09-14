namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Request;

internal class CobaltDownloadRequest
{
    public required string Url { get; set; }

    // Defaults taken from the cobalt API: https://github.com/imputnet/cobalt/blob/main/docs/api.md
    public AudioBitrate AudioBitrate { get; set; } = AudioBitrate.K128;
    public AudioFormat AudioFormat { get; set; } = AudioFormat.Mp3;
    public DownloadMode DownloadMode { get; set; } = DownloadMode.Auto;
    public FilenameStyle FilenameStyle { get; set; } = FilenameStyle.Basic;
    public YoutubeVideoCodec YoutubeVideoCodec { get; set; } = YoutubeVideoCodec.H264;
    public YoutubeVideoContainer YoutubeVideoContainer { get; set; } = YoutubeVideoContainer.Auto;
    public VideoQuality VideoQuality { get; set; } = VideoQuality.K1080;
    public LocalProcessing LocalProcessing { get; set; } = LocalProcessing.Disabled;

    public string? YoutubeDubLang { get; set; }
    public string? SubtitleLang { get; set; }

    public bool DisableMetadata { get; set; } = false;
    public bool AllowH265 { get; set; } = false;
    public bool ConvertGif { get; set; } = true;
    public bool TiktokFullAudio { get; set; } = false;
    public bool AlwaysProxy { get; set; } = false;
    public bool YoutubeHLS { get; set; } = false;
    public bool YoutubeBetterAudio { get; set; } = false;
}