using Dotto.Discord.CommandHandlers.Compress;
using Dotto.Discord.CommandHandlers.Download;
using Dotto.Discord.CommandHandlers.Flags;
using Dotto.Discord.Commands.Compress;
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

        AddMessageListeners(services);
        AddCommandHandlers(services);
        
        return services;
    }

    private static void AddMessageListeners(this IServiceCollection services)
    {
        services.AddScoped<MessageUrlDownload>();
        services.AddScoped<AutoVideoCompressor>();
    }

    private static void AddCommandHandlers(this IServiceCollection services)
    {
        services.AddTransient<DownloadCommandHandler>();
        services.AddTransient<IFlagCommandHandler, FlagCommandHandler>();
        services.AddTransient<CompressCommandHandler>();
    }
}