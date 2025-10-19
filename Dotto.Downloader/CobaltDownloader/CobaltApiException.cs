using Dotto.Infrastructure.Downloader.CobaltDownloader.Response;

namespace Dotto.Infrastructure.Downloader.CobaltDownloader;

internal class CobaltApiException(CobaltErrorResponse response)
    : Exception($"Cobalt returned an error ({response.Error.Context?.Service ?? "cobalt"}: {response.Error.Code})")
{
    public string ErrorCode { get; } = response.Error.Code;
    public string? ServiceName { get; } = response.Error.Context?.Service;
}   