using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dotto.Infrastructure.Downloader.CobaltDownloader.Request;
using Dotto.Infrastructure.Downloader.CobaltDownloader.Response;
using Dotto.Infrastructure.Downloader.Contracts.Interfaces;
using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader;

public class CobaltDownloaderService(
    HttpClient httpClient
    ) : IDownloaderService
{
    private readonly CobaltResponseDeserializer _responseDeserializer = new();
    
    public async Task<IList<DownloadedMedia>> Download(Uri uri, DownloadOptions options, CancellationToken ct = default)
    {
        var requestObject = new CobaltDownloadRequest
        {
            Url = uri.ToString(),
            AllowH265 = true,
            DownloadMode = options.AudioOnly ? DownloadMode.Audio : DownloadMode.Auto
        };

        var jsonString = JsonSerializer.Serialize(requestObject, new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var stringContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

        var resp = await httpClient.PostAsync("/", stringContent, cancellationToken: ct);

        var jsonContent = await resp.Content.ReadAsStringAsync(ct);
        CobaltGenericResponse response;
        
        try
        {
            response = _responseDeserializer.DeserializeToCobaltResponse(jsonContent);
        }
        catch (JsonException ex)
        {
            throw new ApplicationException("Cobalt replied with invalid data", ex);
        }

        return response switch
        {
            CobaltTunnelResponse tr => [await HandleTunnelResponse(tr, ct)],
            CobaltLocalProcessingResponse lpr => [await HandleLocalProcessing(lpr, ct)],
            CobaltErrorResponse er => throw new ApplicationException($"Cobalt returned an error ({er.Error.Code})",
                new Exception($"Service name: {er.Error.Context?.Service}, Error code: {er.Error.Code}")),
            _ => []
        };
    }

    private async Task<DownloadedMedia> HandleTunnelResponse(CobaltTunnelResponse response, CancellationToken ct = default)
    {
        var tunnelResponse = await httpClient.GetAsync(response.Url, ct);
        var length = tunnelResponse.Content.Headers.ContentLength;

        return new DownloadedMedia
        {
            Video = await tunnelResponse.Content.ReadAsStreamAsync(ct),
            FileSize = length,
            Number = 1,
            Metadata = new()
            {
                Title = Path.GetFileNameWithoutExtension(response.Filename),
                Extension = Path.GetExtension(response.Filename)
            },
            VideoFormat = null,
            AudioFormat = null
        };
    }
    
    private async Task<DownloadedMedia> HandleLocalProcessing(CobaltLocalProcessingResponse response, CancellationToken ct = default)
    {
        var tunnels = response.Tunnel;
        
        if (tunnels.Length != 1)
            // sure, it'd make sense if there were multiple files or something.
            // but the "output" member isn't an array; tf am i supposed to do with multiple URLs?
            throw new InvalidDataException("Cobalt returned a locally-processed response with more than 1 tunnel URL. I have no idea how to process that...");
        
        var tunnelResponse = await httpClient.GetAsync(tunnels.Single(), ct);
        var length = tunnelResponse.Content.Headers.ContentLength;

        return new DownloadedMedia
        {
            Video = await tunnelResponse.Content.ReadAsStreamAsync(ct),
            FileSize = length,
            Number = 1,
            Metadata = new()
            {
                Title = response.Output.Metadata?.Title ?? Path.GetFileNameWithoutExtension(response.Output.Filename),
                Extension = response.Output.Metadata?.Title ?? Path.GetExtension(response.Output.Filename)
            },
            VideoFormat = null,
            AudioFormat = null
        };
    }
}