namespace Dotto.Ffmpeg.Contracts;

public interface IVideoCompressorStrategy
{
    Task<CompressionResult> CompressAsync(Stream input, CancellationToken cancellationToken = default);
}
