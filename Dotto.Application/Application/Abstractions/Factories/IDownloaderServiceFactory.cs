using Dotto.Infrastructure.Downloader.Contracts.Interfaces;

namespace Dotto.Application.Abstractions.Factories;

public interface IDownloaderServiceFactory
{
    IEnumerable<IDownloaderService> CreateDownloaderService(Uri uri);
}