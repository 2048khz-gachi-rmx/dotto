using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Request;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DownloadMode
{
    [JsonStringEnumMemberName("auto")]
    Auto,
    [JsonStringEnumMemberName("audio")]
    Audio,
    [JsonStringEnumMemberName("mute")]
    Mute
}