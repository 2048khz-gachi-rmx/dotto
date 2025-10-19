using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Request;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum YoutubeVideoContainer
{
    [JsonStringEnumMemberName("auto")]
    Auto,
    [JsonStringEnumMemberName("mp4")]
    Mp4,
    [JsonStringEnumMemberName("webm")]
    Webm,
    [JsonStringEnumMemberName("mkv")]
    Mkv
}