using Docker.DotNet;
using Dotto.Ai.Agents;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Prompts;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Dotto.Ai.Tools;
using Dotto.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dotto.Ai;

public static class DependencyInjection
{
    internal const string ChatAssistantPromptKey = "ChatAssistant";

    public static IServiceCollection AddAi(this IServiceCollection services, Action<AiSettings> configure)
    {
        services.AddOptions<AiSettings>()
            .Configure(configure)
            .ValidateOnStart();

        services.AddSingleton<IConfigurationOptionSchema>(
            new ConfigurationOptionSchema("Ai", typeof(AiSettings)));
        services.AddSingleton<IConfigurationOptionSchema>(
            new ConfigurationOptionSchema("Ai:Sandbox", typeof(SandboxOptions)));

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
        // Prompt definitions are per-scope configuration; the template contents are not.
        services.AddSingleton<PromptTemplateCache>();
        services.AddScoped<IPromptProvider, PromptProvider>();
    }

    private static void ConfigureChatAssistant(IServiceCollection services)
    {
        services.AddScoped<DiscordTools>();
        services.AddScoped<TerminalTools>();
        services.AddScoped<IAiAgentFactory, AiAgentFactory>();
        services.AddScoped<ChatAssistant>();
    }
}
