using Dotto.Application.InternalServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dotto.Bot.HostedServices;

public class ChannelFlagPoller(IServiceProvider serviceProvider) : BackgroundService
{
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DoPoll();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5 * 60));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DoPoll();
        }
    }

    private DateTime _lastUpdatedTime;
    
    private async Task DoPoll()
    {
        using var scope = serviceProvider.CreateScope();
        var flagsService = scope.ServiceProvider.GetRequiredService<ChannelFlagsService>();
            
        var newTime = await flagsService.UpdateCachedFlags(_lastUpdatedTime);
        _lastUpdatedTime = newTime;
    }
}