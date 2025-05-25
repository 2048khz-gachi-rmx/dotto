using System.Reflection;
using Dotto.Application.InternalServices.ChannelFlagsService;
using Dotto.Application.Modules.Download;
using Dotto.Application.Validation;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Rest;

namespace Dotto.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Add FluentValidationPipelineBehavior so MediatR sends commands through it first
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(FluentValidationPipelineBehavior<,>)
        );
        
        services.AddMediatR(cfg =>
        {
            cfg.RegisterGenericHandlers = true;
            cfg.RegisterServicesFromAssemblies(
                // horrible! https://github.com/jbogard/MediatR/issues/1041#issuecomment-2248940917
                typeof(InteractionMessageProperties).Assembly,
                Assembly.GetExecutingAssembly());
        });
        
        services.AddHybridCache();
        services.AddTransient<ChannelFlagsService>();
        
        services.AddTransient<DownloadCommand>();
        
        return services;
    }
}