using System.ClientModel;
using System.Reflection;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Settings;
using Dotto.Ai.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Dotto.Ai.Agents;

/// <summary>
/// Builds the AI chat client and agent for a specific effective <see cref="AiSettings"/>,
/// so per-interaction configuration (guild/channel/user overrides) reaches the model.
/// </summary>
public interface IAiAgentFactory
{
    IChatClient CreateChatClient(AiSettings settings);

    AIAgent CreateAgent(AiSettings settings);
}

internal sealed class AiAgentFactory : IAiAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<SandboxOptions> _sandboxOptions;
    private readonly Func<AiSettings, IChatClient>? _chatClientFactory;

    public AiAgentFactory(IServiceProvider serviceProvider, IOptions<SandboxOptions> sandboxOptions)
        : this(serviceProvider, sandboxOptions, null)
    {
    }

    /// <summary>Test seam: substitutes the OpenAI client while keeping the real agent wiring.</summary>
    internal AiAgentFactory(
        IServiceProvider serviceProvider,
        IOptions<SandboxOptions> sandboxOptions,
        Func<AiSettings, IChatClient>? chatClientFactory)
    {
        _serviceProvider = serviceProvider;
        _sandboxOptions = sandboxOptions;
        _chatClientFactory = chatClientFactory;
    }
    private static readonly Type[] ToolClasses = [typeof(DiscordTools), typeof(TerminalTools)];

    private static readonly Lazy<IEnumerable<(Type SourceType, (MethodInfo Method, ToolAttribute Attribute)[] Tools)>>
        UserAssistantTools = new(() => GetToolMethods(ToolClasses)
            .Select(group => group with
            {
                Tools = group.Tools
                    .Where(tool => tool.Attribute.ToolType.HasFlag(ToolAttribute.ToolTypeFlag.UserAssistant))
                    .ToArray()
            })
            .Where(group => group.Tools.Length > 0));

    /// <summary>
    /// One-shot guard: the factory is scoped, so logging is deduped across instances.
    /// </summary>
    private static int _sandboxDisabledLogged;

    public IChatClient CreateChatClient(AiSettings settings)
    {
        if (_chatClientFactory != null)
            return _chatClientFactory(settings);

        var (apiKey, baseUrl, model) = ResolveConnection(settings);

        if (apiKey == null)
            throw new InvalidOperationException("Trying to use an AI assistant, but no API key provided.");

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = baseUrl });

        var chatClient = client
            .GetChatClient(model)
            .AsIChatClient();

        var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
        return loggerFactory == null
            ? chatClient
            : new LoggingChatClient(chatClient, loggerFactory.CreateLogger("Dotto.Ai.ChatClient"));
    }

    /// <summary>
    /// Per-assistant values override the top-level ones. Exposed for tests and for consumers
    /// that need to know which model a run will use.
    /// </summary>
    internal static (string? ApiKey, Uri? BaseUrl, string Model) ResolveConnection(AiSettings settings)
        => (settings.ChatAssistant.ApiKey ?? settings.ApiKey,
            settings.ChatAssistant.BaseUrl ?? settings.BaseUrl,
            settings.ChatAssistant.Model ?? settings.AssistantModel);

    public AIAgent CreateAgent(AiSettings settings)
    {
        var sandboxEnabled = _sandboxOptions.Value.Enabled;
        var maxTurns = settings.ChatAssistant.MaxTurns;
        var tools = BuildTools(_serviceProvider, UserAssistantTools.Value, sandboxEnabled, maxTurns);
        var accessor = _serviceProvider.GetService<ContextAccessor>();

        return BuildAgent(
            CreateChatClient(settings),
            tools,
            () => accessor?.Current?.Progress,
            maxTurns);
    }

    /// <summary>
    /// How many requests beyond the turn limit the framework's loop may still make. The model
    /// needs one to see the refusal and one or two to answer; the rest is slack for a model that
    /// argues with the notice. Must stay positive: on its final iteration the framework stops
    /// advertising tools and hands the unanswered call back, which would end up in the answer.
    /// </summary>
    private const int IterationHeadroom = 5;

    /// <summary>
    /// Builds the agent with our own function-invoking client, so the iteration cap belongs to us
    /// and never bites before the turn limit. The tool list is fixed here and never touched again;
    /// the loop is stopped by refusing calls, not by taking tools away.
    /// </summary>
    internal static AIAgent BuildAgent(
        IChatClient chatClient,
        IList<AITool> tools,
        Func<AgentProgress?> progressAccessor,
        int maxTurns)
    {
        var invocationClient = new FunctionInvokingChatClient(
            new TurnTrackerChatClient(chatClient, progressAccessor))
        {
            MaximumIterationsPerRequest = maxTurns + IterationHeadroom
        };

        return new ChatClientAgent(
            invocationClient,
            new ChatClientAgentOptions
            {
                // Without this the agent inserts its own function-invoking client (with the
                // default 40-iteration cap) on top of ours.
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions { Tools = tools }
            });
    }

    /// <summary>
    /// Keeps a tool's return value intact so <see cref="TurnLimitedAIFunction"/> can record a
    /// typed result (exit status, streams); the wrapper, not the factory, decides what text the
    /// model reads.
    /// </summary>
    private static readonly Func<object?, Type?, CancellationToken, ValueTask<object?>> MarshalIdentity =
        (value, _, _) => ValueTask.FromResult(value);

    /// <summary>
    /// Builds the agent's tool list; sandbox-backed tools are dropped when disabled.
    /// </summary>
    internal static List<AITool> BuildTools(
        IServiceProvider sp,
        IEnumerable<(Type SourceType, (MethodInfo Method, ToolAttribute Attribute)[] Tools)> toolGroups,
        bool sandboxEnabled,
        int maxTurns)
    {
        var accessor = sp.GetService<ContextAccessor>();
        var fallbackProgress = new AgentProgress(maxTurns);
        AgentProgress? ResolveProgress() => accessor?.Current?.Progress ?? fallbackProgress;

        if (!sandboxEnabled && Interlocked.Exchange(ref _sandboxDisabledLogged, 1) == 0)
        {
            sp.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(AiAgentFactory))
                .LogWarning(
                    "AI sandbox is disabled (Ai:Sandbox:Enabled=false); " +
                    "sandbox-backed tools are not registered.");
        }

        return toolGroups
            .SelectMany(toolGroup => toolGroup.Tools.Select(tool => (toolGroup.SourceType, Tool: tool)))
            .Where(item => sandboxEnabled || !item.Tool.Attribute.SandboxBacked)
            .Select(item =>
            {
                var function = AIFunctionFactory.Create(
                    method: item.Tool.Method,
                    target: sp.GetRequiredService(item.SourceType),
                    options: new AIFunctionFactoryOptions
                    {
                        Name = item.Tool.Attribute.Name,
                        MarshalResult = MarshalIdentity,
                        // The declarer return type is a record, but the model-visible result is
                        // text, so no object schema should be advertised for it.
                        ExcludeResultSchema = true
                    });

                // One fallback per built agent: if the caller supplied no progress record
                // the turn budget still applies, it is just not visible to anyone.
                return (AITool)new TurnLimitedAIFunction(
                    function, ResolveProgress, item.Tool.Attribute.ExemptFromTurnLimit);
            })
            .ToList();
    }

    private static IEnumerable<(Type SourceType, (MethodInfo Method, ToolAttribute Attribute)[] Tools)> GetToolMethods(
        params Type[] types)
    {
        return types
            .Select(type => (
                SourceType: type,
                Tools: type.GetMethods()
                    .Select(method => (Method: method, Attribute: method.GetCustomAttribute<ToolAttribute>()))
                    .Where(tuple => tuple.Attribute != null)
                    .ToArray()
            ))
            .Where(group => group.Tools.Length > 0)!;
    }
}
