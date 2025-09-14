using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

internal enum CobaltResponseStatus
{
    [JsonStringEnumMemberName("classic")]
    Tunnel = 1,
    
    [JsonStringEnumMemberName("local-processing")]
    LocalProcessing = 2,
    
    [JsonStringEnumMemberName("redirect")]
    Redirect = 3,
    
    [JsonStringEnumMemberName("picker")]
    Picker = 4,
    
    [JsonStringEnumMemberName("error")]
    Error = 5,
}