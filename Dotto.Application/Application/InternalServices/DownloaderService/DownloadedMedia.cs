using Dotto.Application.InternalServices.DownloaderService.Metadata;

namespace Dotto.Application.InternalServices.DownloaderService;

public record DownloadedMedia : IDisposable, IAsyncDisposable
{
    public required Stream Video { get; init; }
    public required int Number { get; init; }
    public required DownloadedMediaMetadata Metadata { get; init; }
    public required FormatData? VideoFormat { get; init; }
    public required FormatData? AudioFormat { get; init; }

    public void Dispose()
    {
        Video.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await Video.DisposeAsync();
    }
}