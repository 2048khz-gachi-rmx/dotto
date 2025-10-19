using Dotto.Common;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace Dotto.Discord.EventHandlers;

[UsedImplicitly]
internal class MessageCreateHandlerCoordinator(IServiceProvider serviceProvider) : IMessageCreateGatewayHandler
{
    private readonly Dictionary<Type, Type[]> _handlerTypes = DiscoverHandlerTypes();

    public async ValueTask HandleAsync(Message arg)
    {
        if (!_handlerTypes.TryGetValue(typeof(Message), out var handlerTypes))
            return;

        var tasks = new ValueTask[handlerTypes.Length];
        var idx = 0;

        using var scope = serviceProvider.CreateScope();
        
        // Launch every handler concurrently
        foreach (var handlerType in handlerTypes)
        {
            var handler = (IGatewayEventProcessor<Message>)scope.ServiceProvider.GetRequiredService(handlerType);
            // blah blah the result should be directly awaited. shush.
#pragma warning disable CA2012
            tasks[idx++] = handler.HandleAsync(arg);
#pragma warning restore CA2012
        }
            
        await ValueTaskUtils.WhenAll(tasks);
    }
    
    private static Dictionary<Type, Type[]> DiscoverHandlerTypes()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetInterfaces()
                .Any(i => i.IsGenericType && 
                         i.GetGenericTypeDefinition() == typeof(IGatewayEventProcessor<>)))
            .GroupBy(t => t.GetInterfaces()
                .First(i => i.GetGenericTypeDefinition() == typeof(IGatewayEventProcessor<>))
                .GetGenericArguments()[0])
            .ToDictionary(g => g.Key, g => g.ToArray());
    }
}