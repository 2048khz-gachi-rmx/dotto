using Dotto.Application;
using Dotto.Common.DateTimeProvider;
using Dotto.Tests.Builders;
using Dotto.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace Dotto.Tests;

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