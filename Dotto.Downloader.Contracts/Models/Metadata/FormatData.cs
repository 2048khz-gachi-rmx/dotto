using System.Text.Json.Serialization;

// Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8618

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global

namespace Dotto.Infrastructure.Downloader.Contracts.Models.Metadata;

public class FormatData
{
    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("manifest_url")]
    public string ManifestUrl { get; set; }

    [JsonPropertyName("ext")]
    public string? Extension { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; }

    [JsonPropertyName("format_id")]
    public string FormatId { get; set; }

    [JsonPropertyName("format_note")]
    public string FormatNote { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    [JsonPropertyName("dynamic_range")]
    public string DynamicRange { get; set; }

    [JsonPropertyName("tbr")]
    public double? Bitrate { get; set; }

    [JsonPropertyName("abr")]
    public double? AudioBitrate { get; set; }

    [JsonPropertyName("acodec")]
    public string? AudioCodec { get; set; }

    [JsonPropertyName("asr")]
    public double? AudioSamplingRate { get; set; }

    [JsonPropertyName("vbr")]
    public double? VideoBitrate { get; set; }

    [JsonPropertyName("fps")]
    public float? FrameRate { get; set; }

    [JsonPropertyName("vcodec")]
    public string? VideoCodec { get; set; }

    [JsonPropertyName("container")]
    public string ContainerFormat { get; set; }

    [JsonPropertyName("filesize")]
    public long? FileSize { get; set; }

    [JsonPropertyName("filesize_approx")]
    public long? ApproximateFileSize { get; set; }

    [JsonPropertyName("player_url")]
    public string PlayerUrl { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; }

    [JsonPropertyName("fragment_base_url")]
    public string FragmentBaseUrl { get; set; }

    [JsonPropertyName("is_from_start")]
    public bool? IsFromStart { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; }

    [JsonPropertyName("quality")]
    public double? Quality { get; set; }

    [JsonPropertyName("stretched_ratio")]
    public float? StretchedRatio { get; set; }

    [JsonPropertyName("no_resume")]
    public bool? NoResume { get; set; }

    [JsonPropertyName("has_drm")]
    public bool? HasDrm { get; set; }
}