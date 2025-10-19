using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Request;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioFormat
{
    [JsonStringEnumMemberName("best")]
    Best,
    [JsonStringEnumMemberName("mp3")]
    Mp3,
    [JsonStringEnumMemberName("ogg")]
    Ogg,
    [JsonStringEnumMemberName("wav")]
    Wav,
    [JsonStringEnumMemberName("opus")]
    Opus
}