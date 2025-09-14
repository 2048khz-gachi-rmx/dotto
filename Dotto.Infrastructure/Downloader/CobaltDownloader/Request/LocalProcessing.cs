using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Request;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LocalProcessing
{
    [JsonStringEnumMemberName("disabled")]
    Disabled,
    [JsonStringEnumMemberName("preferred")]
    Preferred,
    [JsonStringEnumMemberName("forced")]
    Forced
}