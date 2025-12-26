using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dotto.Common;
using Dotto.Common.Exceptions;
using Dotto.Infrastructure.Downloader.CobaltDownloader.Request;
using Dotto.Infrastructure.Downloader.CobaltDownloader.Response;
using Dotto.Infrastructure.Downloader.Contracts.Abstractions;
using Dotto.Infrastructure.Downloader.Contracts.Models;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader;

public class CobaltDownloaderService(HttpClient httpClient) : IDownloaderService
{
    public async Task<IList<DownloadedMedia>> Download(Uri uri, DownloadOptions options, CancellationToken cancellationToken = default)
    {
        var request = new CobaltDownloadRequest
        {
            Url = uri.ToString(),
            AllowH265 = true,
            DownloadMode = options.AudioOnly ? DownloadMode.Audio : DownloadMode.Auto
        };

        CobaltGenericResponse response;
        
        try
        {
            response = await RetryUtils.ExecuteWithRetryAsync(
                async () =>
                {
                    var resp = await GetCobaltResponse(request, cancellationToken);

                    if (resp is CobaltErrorResponse er)
                        throw new CobaltApiException(er);

                    return resp;
                },
                ex => ex is CobaltApiException { ErrorCode: "error.api.fetch.empty" },
                cancellationToken: cancellationToken);
        }
        catch (CobaltApiException ex)
        {
            throw new ApplicationException("Cobalt API Error", ex);
        }
        
        return response switch
        {
            CobaltTunnelResponse tr => [await HandleTunnelResponse(tr, cancellationToken)],
            CobaltLocalProcessingResponse lpr => [await HandleLocalProcessing(lpr, cancellationToken)],
            _ => []
        };
    }

    /*
    // might be unnecessary
    private HttpStatusCode[] CobaltStatusCodes =
    [
        // https://github.com/imputnet/cobalt/blob/main/docs/api.md#possible-http-status-codes
        (HttpStatusCode)200,
        (HttpStatusCode)401,
        (HttpStatusCode)403,
        (HttpStatusCode)404,
        (HttpStatusCode)429,
        (HttpStatusCode)500
    ];
    */

    private static readonly HttpStatusCode[] UnreachableStatusCodes =
    [
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
        (HttpStatusCode)522, // cloudflare
    ];

    private readonly JsonSerializerOptions _serializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    private async Task<CobaltGenericResponse> GetCobaltResponse(CobaltDownloadRequest request, CancellationToken cancellationToken)
    {
        var jsonString = JsonSerializer.Serialize(request, _serializeOptions);

        var stringContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

        var resp = await httpClient.PostAsync("/", stringContent, cancellationToken: cancellationToken);

        if (UnreachableStatusCodes.Contains(resp.StatusCode))
            throw new ServiceUnavailableException("cobalt");
                
        var jsonContent = await resp.Content.ReadAsStringAsync(cancellationToken);
        CobaltGenericResponse response;
        
        try
        {
            response = CobaltResponseDeserializer.DeserializeToCobaltResponse(jsonContent);
        }
        catch (JsonException ex)
        {
            throw new ApplicationException("Cobalt replied with invalid data", ex);
        }

        return response;
    }

    private async Task<DownloadedMedia> HandleTunnelResponse(CobaltTunnelResponse response, CancellationToken ct = default)
    {
        var tunnelResponse = await httpClient.GetAsync(response.Url, ct);
        
        if (!tunnelResponse.IsSuccessStatusCode)
            throw new ApplicationException("Invalid media URL",
                new Exception($"Cobalt replied with a media URL, but actually downloading from it fails ({tunnelResponse.StatusCode}"));
        
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