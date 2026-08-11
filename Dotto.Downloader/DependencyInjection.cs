using Dotto.Common;
using Dotto.Infrastructure.Downloader.CobaltDownloader;
using Dotto.Infrastructure.Downloader.Contracts.Abstractions;
using Dotto.Infrastructure.Downloader.Contracts.Enum;
using Dotto.Infrastructure.Downloader.Settings;
using Dotto.Infrastructure.Downloader.YtdlDownloader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Infrastructure.Downloader;

public static class DependencyInjection
{
    public static IServiceCollection AddDownloader(this IServiceCollection services, Action<DownloaderSettings> configure)
    {
        services.AddOptions<DownloaderSettings>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .Validate<ILogger<DownloaderSettings>>(ValidateYtdlpCookies)
            .ValidateOnStart();
        
        services.AddSingleton(s => s.GetRequiredService<IOptions<DownloaderSettings>>().Value);
        
        ConfigureYtdlService(services);
        ConfigureCobaltService(services);
        
        return services;
    }

    private static bool ValidateYtdlpCookies(DownloaderSettings settings, ILogger<DownloaderSettings> logger)
    {
        if (settings.CookieFile.IsNullOrWhitespace())
            return true;

        if (File.Exists(settings.CookieFile))
            return true;
        
        logger.LogError("Downloader.CookieFile is set to '{CookieFile}' but the file does not exist; yt-dlp will run without cookies.", settings.CookieFile);
        return false;
    }

    private static void ConfigureYtdlService(IServiceCollection services)
    {
        services.AddKeyedSingleton<IDownloaderService, YtdlDownloaderService>(DownloaderType.Ytdl);
    }

    private static void ConfigureCobaltService(IServiceCollection services)
    {
        // hack... we need the options, but we also need to validate them
        {
            var serviceProvider = services.BuildServiceProvider();
            var settings = serviceProvider.GetRequiredService<IOptions<DownloaderSettings>>().Value;
            if (settings.Cobalt == null)
                return;
        }
    
        services.AddHttpClient<CobaltDownloaderService>((httpProvider, client) =>
        {
            var settings = httpProvider.GetRequiredService<IOptions<DownloaderSettings>>().Value;
            
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