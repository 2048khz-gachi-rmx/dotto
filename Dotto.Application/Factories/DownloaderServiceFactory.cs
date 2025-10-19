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
    public IEnumerable<IDownloaderService> CreateDownloaderService(Uri uri)
    {
        if (uri.Host.Contains("instagram"))
        {
            foreach (var downloader in GetInstagramDownloaders())
                yield return downloader;
            
            yield break;
        }
        
        foreach (var downloader in GetGenericDownloaders())
            yield return downloader;
    }

    private IEnumerable<IDownloaderService> GetInstagramDownloaders()
    {
        var cobaltService = serviceProvider.GetKeyedService<IDownloaderService>(DownloaderType.Cobalt);
        
        if (cobaltService != default)
            yield return cobaltService;
        
        yield return serviceProvider.GetRequiredKeyedService<IDownloaderService>(DownloaderType.Ytdl);
    }
    
    private IEnumerable<IDownloaderService> GetGenericDownloaders()
    {
        yield return serviceProvider.GetRequiredKeyedService<IDownloaderService>(DownloaderType.Ytdl);
        
        var cobaltService = serviceProvider.GetKeyedService<IDownloaderService>(DownloaderType.Cobalt);
        
        if (cobaltService != default)
            yield return cobaltService;
    }
}