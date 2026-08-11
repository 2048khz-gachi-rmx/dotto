using Dotto.Application.ChannelFlags;
using Dotto.Application.Factories;
using Dotto.Application.MediaProcessing;
using Dotto.Application.Settings;
using Dotto.Application.VideoProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Dotto.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<UrlCorrectionSettings>()
            .BindConfiguration("UrlCorrection")
            .ValidateOnStart();

        ConfigureFactories(services);
        ConfigureInternalServices(services);


        return services;
    }

    private static void ConfigureInternalServices(IServiceCollection services)
    {
        services.AddHybridCache();
        services.AddScoped<IChannelFlagsService, ChannelFlagsService>();
        services.AddSingleton<IUrlCorrector, UrlCorrector>();
        services.AddSingleton<IMediaDownloader, MediaDownloader>();
        services.AddSingleton<IVideoCompressionService, VideoCompressionService>();
    }

    private static void ConfigureFactories(IServiceCollection services)
    {
        services.AddTransient<IDownloaderServiceFactory, DownloaderServiceFactory>();
    }
}
