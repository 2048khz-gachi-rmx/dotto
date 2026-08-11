using System.ClientModel;
using System.Reflection;
using Docker.DotNet;
using Dotto.Ai.Abstractions;
using Dotto.Ai.Agents;
using Dotto.Ai.Internal;
using Dotto.Ai.Settings;
using Dotto.Ai.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using Type = System.Type;

namespace Dotto.Ai;

public static class DependencyInjection
{
    public const string AssistantKey = "chat_assitant";
    internal const string ChatAssistantPromptKey = "ChatAssistant";

    public static IServiceCollection AddAi(this IServiceCollection services, Action<AiSettings> configure)
    {
        // Eagerly configure to register keyed prompt templates from the Prompts dict
        // TODO: i think we should receive builder.Configuration from the outside
        var settings = new AiSettings { BaseUrl = null!, ApiKey = null!, AssistantModel = null! };
        configure(settings);

        services.AddOptions<AiSettings>()
            .Configure(configure)
            .ValidateOnStart();

        services.AddScoped<ContextAccessor>();
        RegisterPromptTemplates(services, settings);
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
            .ValidateOnStart();
    }

    private static void RegisterSandboxServices(IServiceCollection services)
    {
        // IDockerClient is shared across the scoped Sandbox and the singleton SandboxReaper.
        // The DockerClient implementation is thread-safe and internally pools HTTP connections,
        // so a single instance is appropriate for both.
        services.AddSingleton<IDockerClient>(_ => new DockerClientBuilder().Build());

        // Single scoped sandbox entity per invocation — owns the Docker client,
        // lazily starts the container, and tears it down on scope disposal.
        services.AddScoped<ISandbox, Sandbox>();
        services.AddHostedService<SandboxReaper>();
    }

    private static void RegisterPromptTemplates(IServiceCollection services, AiSettings settings)
    {
        services.AddSingleton<IPromptRenderer, PromptRenderer>();

        foreach (var (key, source) in settings.Prompts)
            services.AddKeyedSingleton<IPromptTemplate>(key, (_, _) => new PromptTemplate(source));
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

                var apiKey = config.ChatAssistant?.ApiKey ?? config.ApiKey;
                var baseUrl = config.ChatAssistant?.BaseUrl ?? config.BaseUrl;
                var model = config.ChatAssistant?.Model ?? config.AssistantModel;

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
            var tools = userAssistantTools
                .SelectMany(toolType =>
                    toolType.Tools.Select(tool => AIFunctionFactory.Create(
                        method: tool.Method,
                        target: sp.GetRequiredService(toolType.SourceType),
                        name: tool.Attribute.Name)))
                .Cast<AITool>()
                .ToList();

            return new ChatClientAgent(
                chatClient: sp.GetRequiredKeyedService<IChatClient>(key),
                tools: tools
            );
        });

        services.AddScoped<ChatAssistant>();
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

    private static Delegate WrapInResolver(Type sourceType, MethodInfo method, ToolAttribute attribute)
    {
        return () => { };
    }
}