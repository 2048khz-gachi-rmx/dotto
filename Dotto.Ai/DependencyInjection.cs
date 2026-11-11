using System.ClientModel;
using System.Reflection;
using Docker.DotNet;
using Dotto.Ai.Agents;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Prompts;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Dotto.Ai.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Dotto.Ai;

public static class DependencyInjection
{
    public const string AssistantKey = "chat_assitant";
    internal const string ChatAssistantPromptKey = "ChatAssistant";

    /// <summary>
    /// One-shot guard: the agent factory is transient, so logging is deduped.
    /// </summary>
    private static int _sandboxDisabledLogged;

    public static IServiceCollection AddAi(this IServiceCollection services, Action<AiSettings> configure)
    {
        services.AddOptions<AiSettings>()
            .Configure(configure)
            .ValidateOnStart();

        services.AddScoped<ContextAccessor>();
        RegisterPromptTemplates(services);
        ConfigureChatAssistant(services);
        RegisterSandboxOptions(services);
        RegisterSandboxServices(services);

        return services;
    }

    private static void RegisterSandboxOptions(IServiceCollection services)
    {
        services.AddOptions<SandboxOptions>()
            .BindConfiguration("Ai:Sandbox")
            .ValidateDataAnnotations()
            .Validate(
                o => o.MemoryMb > o.WorkTmpfsMb,
                "Ai:Sandbox:MemoryMb must be larger than Ai:Sandbox:WorkTmpfsMb " +
                "(tmpfs pages count against the container memory cgroup).")
            .Validate(
                o => o.DnsServers.All(ip => System.Net.IPAddress.TryParse(ip, out _)),
                "Ai:Sandbox:DnsServers must contain only IP addresses.")
            .ValidateOnStart();
    }

    private static void RegisterSandboxServices(IServiceCollection services)
    {
        // Singleton: DockerClient is thread-safe and pools connections.
        services.AddSingleton<IDockerClient>(_ => new DockerClientBuilder().Build());

        services.AddScoped<ISandbox, Sandbox.Sandbox>();
        services.AddHostedService<SandboxReaper>();
    }

    private static void RegisterPromptTemplates(IServiceCollection services)
    {
        services.AddSingleton<IPromptProvider, PromptProvider>();
    }

    private static void ConfigureChatAssistant(IServiceCollection services)
    {
        var toolClasses = new[] { typeof(DiscordTools), typeof(TerminalTools) };
        var toolMethods = GetToolMethods(toolClasses);
        
        foreach (var toolClass in toolClasses)
            services.AddScoped(toolClass);

        var userAssistantTools = toolMethods
            .Select(g => g with
            {
                Tools = g.Tools
                    .Where(t => t.Attribute.ToolType.HasFlag(ToolAttribute.ToolTypeFlag.UserAssistant))
                    .ToArray()
            })
            .Where(g => g.Tools.Length > 0);

        services.AddKeyedChatClient(AssistantKey,
            sp =>
            {
                var config = sp.GetRequiredService<IOptions<AiSettings>>().Value;

                var apiKey = config.ChatAssistant.ApiKey ?? config.ApiKey;
                var baseUrl = config.ChatAssistant.BaseUrl ?? config.BaseUrl;
                var model = config.ChatAssistant.Model ?? config.AssistantModel;

                if (apiKey == null)
                    throw new InvalidOperationException("Trying to use an AI assistant, but no API key provided.");

                var client = new OpenAIClient(
                    new ApiKeyCredential(apiKey),
                    new OpenAIClientOptions { Endpoint = baseUrl });

                return client
                    .GetChatClient(model)
                    .AsIChatClient();
            }, lifetime: ServiceLifetime.Singleton).UseLogging();

        services.AddKeyedTransient<AIAgent>(AssistantKey, (sp, key) =>
        {
            var sandboxEnabled = sp.GetRequiredService<IOptions<SandboxOptions>>().Value.Enabled;
            var tools = BuildTools(sp, userAssistantTools, sandboxEnabled);

            return new ChatClientAgent(
                chatClient: sp.GetRequiredKeyedService<IChatClient>(key),
                tools: tools
            );
        });

        services.AddScoped<ChatAssistant>();
    }

    /// <summary>
    /// Builds the agent's tool list; sandbox-backed tools are dropped when disabled.
    /// </summary>
    internal static List<AITool> BuildTools(
        IServiceProvider sp,
        IEnumerable<(Type SourceType, (MethodInfo Method, ToolAttribute Attribute)[] Tools)> toolGroups,
        bool sandboxEnabled)
    {
        var maxTurns = sp.GetService<IOptions<AiSettings>>()?.Value.ChatAssistant.MaxTurns ?? 20;
        var accessor = sp.GetService<ContextAccessor>();
        var fallbackProgress = new AgentProgress(maxTurns);
        AgentProgress? ResolveProgress() => accessor?.Current?.Progress ?? fallbackProgress;

        if (!sandboxEnabled && Interlocked.Exchange(ref _sandboxDisabledLogged, 1) == 0)
        {
            sp.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(DependencyInjection))
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
                    name: item.Tool.Attribute.Name);

                // One fallback per built agent: if the caller supplied no progress record
                // the turn budget still applies, it is just not visible to anyone.
                return (AITool)new TurnLimitedAIFunction(function, ResolveProgress);
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
            .Where(g => g.Tools.Length > 0)!;
    }
}