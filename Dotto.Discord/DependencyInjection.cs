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

        AddCommands(services, discordSection);
        AddCommandHandlers(services, discordSection);
        
        return services;
    }

    public static void AddCommands(this IServiceCollection services, IConfigurationSection discordSection)
    {
        services.AddScoped<MessageUrlDownload>();
    }
    
    public static void AddCommandHandlers(this IServiceCollection services, IConfigurationSection discordSection)
    {
        services.AddTransient<DownloadCommandHandler>();
    }
}