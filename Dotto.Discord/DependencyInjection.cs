using Dotto.Discord.CommandHandlers.Download;
using Dotto.Discord.Commands.Download;
using Dotto.Discord.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dotto.Discord;

public static class DependencyInjection
{
    public static IServiceCollection AddDiscordIntegration(this IServiceCollection services, IConfigurationSection discordSection)
    {
        services.AddOptions<AutoDownloadSettings>()
            .Bind(discordSection.GetSection("AutoDownload"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddSingleton(s => s.GetRequiredService<IOptions<AutoDownloadSettings>>().Value);

        AddCommands(services);
        AddCommandHandlers(services);
        
        return services;
    }

    private static void AddCommands(this IServiceCollection services)
    {
        services.AddScoped<MessageUrlDownload>();
    }

    private static void AddCommandHandlers(this IServiceCollection services)
    {
        services.AddTransient<DownloadCommandHandler>();
    }
}