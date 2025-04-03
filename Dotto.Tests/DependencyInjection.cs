using Dotto.Application;
using Dotto.Common.DateTimeProvider;
using Dotto.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Tests.Builders;
using Tests.Mocks;

namespace Tests;

public static class DependencyInjection
{
    public static IServiceCollection AddTestServices(this IServiceCollection services)
    {
        services.AddSingleton<TestDateTimeProvider>();
        services.AddSingleton<IDateTimeProvider, TestDateTimeProvider>();
        
        services.AddTransient<ChannelFlagBuilder>();

        services.AddDatabase(TestRun.GetConnectionString());
        services.AddApplication();
        
        return services;
    }
    
    public static ServiceProvider BuildNewServiceProvider()
    {
        var provider = new ServiceCollection();
        provider.AddTestServices();
        
        return provider.BuildServiceProvider();
    }
}