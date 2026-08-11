using Dotto.Infrastructure.Downloader.Contracts.Abstractions;

namespace Dotto.Application.Factories;

public interface IDownloaderServiceFactory
{
    IEnumerable<IDownloaderService> CreateDownloaderService(string query);
}