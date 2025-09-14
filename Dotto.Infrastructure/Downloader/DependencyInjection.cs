using Dotto.Infrastructure.Downloader.CobaltDownloader;
using Dotto.Infrastructure.Downloader.Contracts.Enum;
using Dotto.Infrastructure.Downloader.Contracts.Interfaces;
using Dotto.Infrastructure.Downloader.Settings;
using Dotto.Infrastructure.Downloader.YtdlDownloader;
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
        
        SetupYtdlService(services);
        SetupCobaltService(services);
        
        return services;
    }

    private static void SetupYtdlService(IServiceCollection services)
    {
        services.AddKeyedSingleton<IDownloaderService, YtdlDownloaderService>(DownloaderType.Ytdl, (isp, _) => isp.GetRequiredService<YtdlDownloaderService>());
    }

    private static void SetupCobaltService(IServiceCollection services)
    {
        // hack... we need the options, but we also need to validate them
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<DownloaderSettings>>().Value;

        if (options.Cobalt == null)
            return;
    
        services.AddHttpClient<CobaltDownloaderService>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<DownloaderSettings>>().Value;
            
            client.DefaultRequestHeaders.Add("Authorization", $"Api-Key {settings.Cobalt!.ApiKey}");
            client.BaseAddress = settings.Cobalt.BaseUrl;
            
            // Required by cobalt, as per the docs
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // workaround: injecting the httpclient by interface into the service and then trying to resolve it by key won't work;
        // the client's configuration won't run. instead, we inject the httpclient into the concrete implementation,
        // then add the implementation factory to the keyed singleton which actually just resolves the concrete implementation under the hood. 
        // afaik this appears to only be an issue if your key isn't a string; if it was, you could just use httpclient's tools (like AddAsKeyed)
        services.AddKeyedSingleton<IDownloaderService, CobaltDownloaderService>(DownloaderType.Cobalt, (isp, _) => isp.GetRequiredService<CobaltDownloaderService>());
    }
}