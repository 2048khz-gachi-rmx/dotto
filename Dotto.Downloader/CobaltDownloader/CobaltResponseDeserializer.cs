using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dotto.Common;
using Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader;

internal class CobaltResponseDeserializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter<CobaltResponseStatus>(JsonNamingPolicy.CamelCase)
        }
    };

    public static CobaltGenericResponse DeserializeToCobaltResponse(string responseJsonBody)
    {
        if (responseJsonBody.IsNullOrWhitespace())
            throw new JsonException("Cobalt returned an empty response");
        
        // Debug: Log the raw JSON being deserialized
        Debug.WriteLine($"Deserializing JSON: {responseJsonBody}");
        
        var response = JsonSerializer.Deserialize<CobaltGenericResponse>(responseJsonBody, JsonOptions);

        if (response == null)
            throw new JsonException("Cobalt returned a response that doesn't match their generic response format");
        
        CobaltGenericResponse? ret = response.Status switch
        {
            CobaltResponseStatus.Tunnel or CobaltResponseStatus.Redirect => JsonSerializer.Deserialize<CobaltTunnelResponse>(responseJsonBody, JsonOptions),
            CobaltResponseStatus.LocalProcessing => JsonSerializer.Deserialize<CobaltLocalProcessingResponse>(responseJsonBody, JsonOptions),
            CobaltResponseStatus.Picker => JsonSerializer.Deserialize<CobaltPickerResponse>(responseJsonBody, JsonOptions),
            CobaltResponseStatus.Error => JsonSerializer.Deserialize<CobaltErrorResponse>(responseJsonBody, JsonOptions),
            _ => throw new ArgumentOutOfRangeException(null, response.Status, "Unhandled Cobalt response type")
        };
        
        if (ret == null)
            throw new JsonException($"Cobalt returned a response which couldn't be deserialized to a {response.Status} response");

        return ret;
    }
}