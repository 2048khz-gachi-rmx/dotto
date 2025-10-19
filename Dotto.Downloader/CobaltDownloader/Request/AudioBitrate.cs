using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Request;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioBitrate
{
    [JsonStringEnumMemberName("320")]
    K320,
    [JsonStringEnumMemberName("256")]
    K256,
    [JsonStringEnumMemberName("128")]
    K128,
    [JsonStringEnumMemberName("96")]
    K96,
    [JsonStringEnumMemberName("64")]
    K64,
    [JsonStringEnumMemberName("8")]
    K8
}