using Dotto.Application.Modules.Download;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Commands.Download;

public class SlashCommand(DownloadCommand dl) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("dl", "Download from URL via yt-dlp")]
    public async Task InvokeSlash(string uriString)
    {
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            await RespondAsync(InteractionCallback.Message(new() { Content = "No URL matched" }));
            return;
        }

        // if an unhandled exception occurs, NetCord will acknowledge the command with an error instead of following up,
        // which will give us a 400 Bad Request by discord. so let's check for synchronous errors first
        var hydrateTask = dl.CreateMessage<InteractionMessageProperties>(uri);
        if (hydrateTask.IsFaulted)
        {
            throw hydrateTask.Exception;
        }
        
        var respTask = RespondAsync(InteractionCallback.DeferredMessage());

        await respTask;
        await FollowupAsync(await hydrateTask);
    }
}