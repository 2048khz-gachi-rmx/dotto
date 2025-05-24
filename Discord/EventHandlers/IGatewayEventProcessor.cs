namespace Dotto.Discord.EventHandlers;

/// <summary>
/// The difference between an EventProcessor and Netcord's EventHandler is that EventProcessors are scoped,
/// and are instantiated per-request (like application/text commands), unlike EventHandlers which are singletons.
/// </summary>
public interface IGatewayEventProcessor<T>
{
    ValueTask HandleAsync(T arg);
}