using Dotto.Infrastructure.Downloader.Contracts.Models.Metadata;

namespace Dotto.Infrastructure.Downloader.Contracts.Models;

public record DownloadedMedia : IDisposable, IAsyncDisposable
{
    public required Stream Video { get; init; }
    public required long? FileSize { get; init; }

    /// <summary>
    /// Video index. Starts at 1
    /// </summary>
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
    {
        var extension = (VideoFormat ?? AudioFormat)?.Extension ?? "mp4";
        
        if (VideoFormat == null && AudioFormat?.AudioCodec == "opus")
        {
            // HACK: discord doesn't embed webm's with audio-only
            extension = "opus";
        }

        return extension;
    }

    public string GetFileName()
        => (Metadata.Title ?? Guid.NewGuid().ToString("N")) + $".{GetExtension()}";

    public string GetResolution()
        => VideoFormat?.Resolution != null
            ? VideoFormat?.Resolution ?? "unknown resolution"
            : AudioFormat?.AudioBitrate != null
                ? $"{Math.Ceiling(AudioFormat.AudioBitrate.Value)}kbps"
                : "unknown bitrate";

    public string GetCodec()
        => VideoFormat?.VideoCodec != null
            ? VideoFormat.VideoCodec ?? "unknown codec"
            : AudioFormat?.AudioCodec ?? "unknown codec";
}