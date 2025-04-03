using Dotto.Application.Modules.Download;
using Dotto.Commands.Download;
using Microsoft.Extensions.DependencyInjection;

namespace Dotto.Commands;

public static class DependencyInjection
{
    public static IServiceCollection AddCommandServices(this IServiceCollection services)
    {
        services.AddSingleton<DownloadCommand>();
        
        return services;
    }
}