using Dotto.Infrastructure.Downloader.Contracts.Abstractions;

namespace Dotto.Application.Abstractions.Factories;

public interface IDownloaderServiceFactory
{
    IEnumerable<IDownloaderService> CreateDownloaderService(Uri uri);
}