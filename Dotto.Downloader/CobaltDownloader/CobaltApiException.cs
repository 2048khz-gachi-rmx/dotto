using Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader;

class CobaltApiException : Exception
{
    public string ErrorCode { get; }
    public string? ServiceName { get; }
    
    public CobaltApiException(CobaltErrorResponse response)
        : base($"Cobalt returned an error ({response.Error.Context?.Service ?? "cobalt"}: {response.Error.Code})")
    {
        ErrorCode = response.Error.Code;
        ServiceName = response.Error.Context?.Service;
    }
}   