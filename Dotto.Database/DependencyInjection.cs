using Dotto.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dotto.Infrastructure.Database;

public static class DependencyInjection
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, string? connectionString)
    {
        if (connectionString == null)
        {
            return services;
        }
        
        services.AddNpgsql<DottoDbContext>(connectionString);
        services.AddTransient<IDottoDbContext>(isp => isp.GetRequiredService<DottoDbContext>());
        return services;
    }
    
    public static async Task<IHost> MigrateDatabase(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        
        var db = scope.ServiceProvider.GetRequiredService<DottoDbContext>();
        await db.Database.MigrateAsync();
        
        return host;
    }
}