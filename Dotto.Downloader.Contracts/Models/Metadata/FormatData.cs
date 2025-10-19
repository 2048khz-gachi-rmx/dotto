using System.Text.Json.Serialization;

// Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8618

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global

namespace Dotto.Infrastructure.Downloader.Contracts.Models.Metadata;

public class FormatData
{
    [JsonPropertyName("url")]
    public string Url { get; init; }

    [JsonPropertyName("manifest_url")]
    public string ManifestUrl { get; init; }

    [JsonPropertyName("ext")]
    public string? Extension { get; init; }

    [JsonPropertyName("format")]
    public string Format { get; init; }

    [JsonPropertyName("format_id")]
    public string FormatId { get; init; }

    [JsonPropertyName("format_note")]
    public string FormatNote { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }

    [JsonPropertyName("dynamic_range")]
    public string DynamicRange { get; init; }

    [JsonPropertyName("tbr")]
    public double? Bitrate { get; init; }

    [JsonPropertyName("abr")]
    public double? AudioBitrate { get; init; }

    [JsonPropertyName("acodec")]
    public string? AudioCodec { get; init; }

    [JsonPropertyName("asr")]
    public double? AudioSamplingRate { get; init; }

    [JsonPropertyName("vbr")]
    public double? VideoBitrate { get; init; }

    [JsonPropertyName("fps")]
    public float? FrameRate { get; init; }

    [JsonPropertyName("vcodec")]
    public string? VideoCodec { get; init; }

    [JsonPropertyName("container")]
    public string ContainerFormat { get; init; }

    [JsonPropertyName("filesize")]
    public long? FileSize { get; init; }

    [JsonPropertyName("filesize_approx")]
    public long? ApproximateFileSize { get; init; }

    [JsonPropertyName("player_url")]
    public string PlayerUrl { get; init; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; init; }

    [JsonPropertyName("fragment_base_url")]
    public string FragmentBaseUrl { get; init; }

    [JsonPropertyName("is_from_start")]
    public bool? IsFromStart { get; init; }

    [JsonPropertyName("language")]
    public string Language { get; init; }

    [JsonPropertyName("quality")]
    public double? Quality { get; init; }

    [JsonPropertyName("stretched_ratio")]
    public float? StretchedRatio { get; init; }

    [JsonPropertyName("no_resume")]
    public bool? NoResume { get; init; }

    [JsonPropertyName("has_drm")]
    public bool? HasDrm { get; init; }
}