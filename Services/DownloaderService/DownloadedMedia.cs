namespace Services.DownloaderService;

public record DownloadedMedia : IDisposable, IAsyncDisposable
{
    public required Stream Video { get; init; }
    public required DownloadedMediaMetadata Metadata { get; init; }

    public void Dispose()
    {
        Video.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Video.DisposeAsync();
    }
}