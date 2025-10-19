using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Request;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilenameStyle
{
    [JsonStringEnumMemberName("classic")]
    Classic,
    [JsonStringEnumMemberName("pretty")]
    Pretty,
    [JsonStringEnumMemberName("basic")]
    Basic,
    [JsonStringEnumMemberName("nerdy")]
    Nerdy
}