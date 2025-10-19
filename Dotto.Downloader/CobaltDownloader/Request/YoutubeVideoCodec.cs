using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Request;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum YoutubeVideoCodec
{
    [JsonStringEnumMemberName("h264")]
    H264,
    [JsonStringEnumMemberName("av1")]
    Av1,
    [JsonStringEnumMemberName("vp9")]
    Vp9
}
