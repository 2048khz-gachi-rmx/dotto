using Dotto.Ffmpeg.Contracts;
using Dotto.Ffmpeg.Services;
using Dotto.Ffmpeg.Settings;
using Dotto.Ffmpeg.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace Dotto.Ffmpeg;

public static class FfmpegDependencyInjection
{
    public static IServiceCollection AddFfmpeg(this IServiceCollection services)
    {
        services.AddOptions<CompressionSettings>()
            .BindConfiguration("Compress")
            .ValidateOnStart();

        services.AddSingleton<FfmpegService>();
        services.AddHostedService<FfmpegTempCleanupService>();

        services.AddKeyedScoped<IVideoCompressorStrategy, Vp9CompressionStrategy>(CompressionMethod.Vp9);
        services.AddKeyedScoped<IVideoCompressorStrategy, Av1CompressionStrategy>(CompressionMethod.Av1);

        return services;
    }
}
