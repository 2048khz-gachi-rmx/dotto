namespace Dotto.Ai.Agents.Base;

/// <summary>
/// Horrible crutch hack. Tools may require state (specifically, so they know the context under which the agent was invoked),
/// and you can't just go ahead and register the context in a scope. So we do register this accessor,
/// and before invoking the agent, resolve it and modify it. Ew.
/// See also: Microsoft's HttpContextAccessor
/// </summary>
public class ContextAccessor
{
    private readonly Dictionary<Type, object> _contexts = new();

    public T Get<T>() where T : AgentContext =>
        _contexts.TryGetValue(typeof(T), out var ctx)
            ? (T)ctx
            : throw new InvalidOperationException($"No {typeof(T).Name} context set.");

    public void Set<T>(T context) where T : AgentContext =>
        _contexts[typeof(T)] = context;
}