using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Commands.Download;

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

        var respTask = RespondAsync(InteractionCallback.DeferredMessage());

        var hydrateTask = dl.HydrateMessage(uri, new InteractionMessageProperties());

        await respTask;
        await FollowupAsync(await hydrateTask);
    }
}