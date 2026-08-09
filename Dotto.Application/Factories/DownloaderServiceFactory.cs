using Dotto.Application.Abstractions.Factories;
using Dotto.Infrastructure.Downloader.Contracts.Abstractions;
using Dotto.Infrastructure.Downloader.Contracts.Enum;
using Microsoft.Extensions.DependencyInjection;

namespace Dotto.Application.Factories;

public class DownloaderServiceFactory(IServiceProvider serviceProvider) : IDownloaderServiceFactory
{
    public IEnumerable<IDownloaderService> CreateDownloaderService(string query)
    {
        // Cobalt only supports plain video URLs, so if the query isn't a valid URL
        // (e.g. yt-dlp queries like "ytsearch:..."), only yt-dlp can handle it.
        if (!Uri.TryCreate(query, UriKind.Absolute, out var uri))
            return GetGenericDownloaders();

        return uri.Host.Contains("instagram")
            ? GetInstagramDownloaders()
            : GetGenericDownloaders();
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