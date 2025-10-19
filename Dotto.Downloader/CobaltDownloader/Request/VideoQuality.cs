using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Request;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VideoQuality
{
    [JsonStringEnumMemberName("max")]
    Max,
    [JsonStringEnumMemberName("4320")]
    K4320,
    [JsonStringEnumMemberName("2160")]
    K2160,
    [JsonStringEnumMemberName("1440")]
    K1440,
    [JsonStringEnumMemberName("1080")]
    K1080,
    [JsonStringEnumMemberName("720")]
    K720,
    [JsonStringEnumMemberName("480")]
    K480,
    [JsonStringEnumMemberName("360")]
    K360,
    [JsonStringEnumMemberName("240")]
    K240,
    [JsonStringEnumMemberName("144")]
    K144
}