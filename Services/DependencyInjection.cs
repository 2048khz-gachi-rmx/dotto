using Microsoft.Extensions.DependencyInjection;
using Services.DownloaderService;

namespace Services;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IDownloaderService, YtdlDownloaderService>();
        
        return services;
    }
}