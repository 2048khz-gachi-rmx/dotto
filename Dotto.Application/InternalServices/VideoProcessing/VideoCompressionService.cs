using Dotto.Application.Abstractions.VideoProcessing;
using Dotto.Ffmpeg.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Dotto.Application.InternalServices.VideoProcessing;

internal class VideoCompressionService(
    IServiceScopeFactory scopeFactory)
    : IVideoCompressionService
{
    public async Task<CompressionResult> CompressVideoAsync(
        Stream videoStream, 
        CompressionOptions options, 
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var strategy = scope.ServiceProvider.GetRequiredKeyedService<IVideoCompressorStrategy>(options.Method);
        return await strategy.CompressAsync(videoStream, cancellationToken);
    }
}
