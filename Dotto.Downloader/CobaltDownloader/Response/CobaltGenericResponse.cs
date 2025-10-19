using System.Text.Json.Serialization;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

internal class CobaltGenericResponse
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CobaltResponseStatus Status { get; set; }
}