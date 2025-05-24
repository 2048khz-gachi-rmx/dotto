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

        services.AddScoped<MessageUrlDownload>();
        
        return services;
    }
}