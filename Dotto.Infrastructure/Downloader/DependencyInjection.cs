using Dotto.Application.InternalServices.DownloaderService;
using Microsoft.Extensions.DependencyInjection;

namespace Dotto.Infrastructure.Downloader;

public static class DependencyInjection
{
    public static IServiceCollection AddDownloader(this IServiceCollection services)
    {
        services.AddSingleton<IDownloaderService, YtdlDownloaderService>();
        
        return services;
    }
}