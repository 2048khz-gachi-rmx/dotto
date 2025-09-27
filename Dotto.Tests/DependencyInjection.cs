using Dotto.Application;
using Dotto.Common.DateTimeProvider;
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

        services.AddApplication();
        
        return services;
    }
    
    public static ServiceCollection BuildNewServiceCollection()
    {
        var collection = new ServiceCollection();
        collection.AddTestServices();

        return collection;
    }
}