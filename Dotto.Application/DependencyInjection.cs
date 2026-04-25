using System.Reflection;
using Dotto.Application.Abstractions.Factories;
using Dotto.Application.Abstractions.MediaProcessing;
using Dotto.Application.Factories;
using Dotto.Application.InternalServices;
using Dotto.Application.InternalServices.MediaProcessing;
using Dotto.Application.Settings;
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
        services.AddSingleton<IMediaProcessingService, MediaProcessingService>();
    }
    
    private static void ConfigureFactories(IServiceCollection services)
    {
        services.AddTransient<IDownloaderServiceFactory, DownloaderServiceFactory>();
    }
}