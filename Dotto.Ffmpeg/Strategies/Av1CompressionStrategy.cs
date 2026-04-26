using Dotto.Ffmpeg.Contracts;

namespace Dotto.Ffmpeg.Strategies;

 internal class Av1CompressionStrategy : IVideoCompressorStrategy
{
    private const string Extension = ".webm";

    public Task<CompressionResult> CompressAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CompressionResult(
            Stream.Null,
            0,
            0,
            false,
            "AV1 compression is not yet implemented",
            Extension));
    }
}
