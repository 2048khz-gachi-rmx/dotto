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

    public string GetExtension()
        => (VideoFormat ?? AudioFormat)!.Extension ?? "mp4";

    public string GetFileName()
        => (Metadata.Title ?? Guid.NewGuid().ToString("N")) + $".{GetExtension()}";

    public string GetResolution()
        => VideoFormat?.Resolution != null
            ? VideoFormat?.Resolution ?? "unknown resolution"
            : AudioFormat?.AudioBitrate != null
                ? $"{Math.Ceiling(AudioFormat.AudioBitrate.Value / 1024)}kb/s"
                : "unknown bitrate";

    public string GetCodec()
        => VideoFormat?.VideoCodec != null
            ? VideoFormat.VideoCodec ?? "unknown codec"
            : AudioFormat?.AudioCodec ?? "unknown codec";
}