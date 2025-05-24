using Dotto.Application.InternalServices.DownloaderService;
using Dotto.Infrastructure.Downloader.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dotto.Infrastructure.Downloader;

public static class DependencyInjection
{
    public static IServiceCollection AddDownloader(this IServiceCollection services, IConfigurationSection downloaderSettings)
    {
        services.AddOptions<DownloaderSettings>()
            .Bind(downloaderSettings)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddSingleton(s => s.GetRequiredService<IOptions<DownloaderSettings>>().Value);
        
        services.AddSingleton<IDownloaderService, YtdlDownloaderService>();
        
        return services;
    }
}