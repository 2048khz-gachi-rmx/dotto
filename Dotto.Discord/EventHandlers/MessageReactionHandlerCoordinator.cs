using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace Dotto.Discord.EventHandlers;

[UsedImplicitly]
public class MessageReactionHandlerCoordinator(IServiceProvider serviceProvider) : IMessageReactionAddGatewayHandler
{
    private readonly Dictionary<Type, Type[]> _handlerTypes = DiscoverHandlerTypes();

    public async ValueTask HandleAsync(MessageReactionAddEventArgs args)
    {
        if (!_handlerTypes.TryGetValue(typeof(MessageReactionAddEventArgs), out var handlerTypes))
            return;

        var tasks = new Task[handlerTypes.Length];
        var idx = 0;

        using var scope = serviceProvider.CreateScope();

        foreach (var handlerType in handlerTypes)
        {
            var handler = (IGatewayEventProcessor<MessageReactionAddEventArgs>)scope.ServiceProvider.GetRequiredService(handlerType);
            tasks[idx++] = handler.HandleAsync(args).AsTask();
        }

        await Task.WhenAll(tasks);
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
