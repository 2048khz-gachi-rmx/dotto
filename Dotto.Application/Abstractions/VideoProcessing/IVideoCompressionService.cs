using Dotto.Ffmpeg.Contracts;

namespace Dotto.Application.Abstractions.VideoProcessing;

public interface IVideoCompressionService
{
    Task<CompressionResult> CompressVideoAsync(Stream videoStream, CompressionOptions options, CancellationToken cancellationToken = default);
}
