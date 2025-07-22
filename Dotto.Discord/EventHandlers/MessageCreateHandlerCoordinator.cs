using Dotto.Common;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace Dotto.Discord.EventHandlers;

internal class MessageCreateHandlerCoordinator(IServiceProvider serviceProvider) : IMessageCreateGatewayHandler
{
    private readonly Dictionary<Type, Type[]> _handlerTypes = DiscoverHandlerTypes();

    public async ValueTask HandleAsync(Message arg)
    {
        if (!_handlerTypes.TryGetValue(typeof(Message), out var handlerTypes))
            return;

        var tasks = new ValueTask[handlerTypes.Length];
        int idx = 0;

        using var scope = serviceProvider.CreateScope();
        
        // Launch every handler concurrently
        foreach (var handlerType in handlerTypes)
        {
            var handler = (IGatewayEventProcessor<Message>)scope.ServiceProvider.GetRequiredService(handlerType);
            tasks[idx++] = handler.HandleAsync(arg);
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