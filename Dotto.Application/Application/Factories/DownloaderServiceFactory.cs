using Dotto.Application.Abstractions.Factories;
using Dotto.Infrastructure.Downloader.Contracts.Enum;
using Dotto.Infrastructure.Downloader.Contracts.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dotto.Application.Factories;

public class DownloaderServiceFactory(
    IServiceProvider serviceProvider,
    ILogger<DownloaderServiceFactory> logger
    )
    : IDownloaderServiceFactory
{
    public IDownloaderService CreateDownloaderService(Uri uri)
    {
        DownloaderType type = DownloaderType.Ytdl;

        if (uri.Host.Contains("instagram"))
            type = DownloaderType.Cobalt;

        var service = serviceProvider.GetKeyedService<IDownloaderService>(type);

        if (service == default)
        {
            logger.LogWarning($"Downloader type {type} failed to resolve; perhaps it's not set up?");
            service = serviceProvider.GetRequiredKeyedService<IDownloaderService>(DownloaderType.Ytdl);
        }
        
        return service;
    }
}