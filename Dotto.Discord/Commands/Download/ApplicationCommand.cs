using Dotto.Application.Modules.Download;
using Dotto.Common;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Commands.Download;

public class ApplicationCommand(DownloadCommand dl) : ApplicationCommandModule<ApplicationCommandContext>
{
    private async Task Download(string uriString, bool isSilent)
    {
        var flags = isSilent ? MessageFlags.Ephemeral : default;
        
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            await RespondAsync(InteractionCallback.Message(new() { Content = "No URL matched", Flags = flags }));
            return;
        }

        // if an unhandled exception occurs, NetCord will acknowledge the command with an error instead of following up,
        // which will give us a 400 Bad Request by discord. so let's check for synchronous errors first
        var uploadLimit = DownloadCommand.GetMaxDiscordFileSize(Context.Guild, Context.User);
        var hydrateTask = dl.CreateMessage<InteractionMessageProperties>(uri, uploadLimit);
        if (hydrateTask.IsFaulted)
        {
            throw hydrateTask.Exception;
        }
        
        var respTask = RespondAsync(InteractionCallback.DeferredMessage(flags));

        await respTask;

        var dlResponse = await hydrateTask;
        var newMessage = await FollowupAsync(dlResponse.Message);

        await dl.LogDownloadedMedia(newMessage, dlResponse, Context.User, uri);
    }

    [SlashCommand("dl", "Download from URL via yt-dlp")]
    public Task InvokeSlash(
        [SlashCommandParameter(Name = "url", Description = "Link to the media you want to download")]
        string uriString,
        [SlashCommandParameter(Name = "private", Description = "Should the downloaded video be hidden from others?")]
        bool isSilent = false)
        => Download(uriString, isSilent);

    private Task ParseAndDownload(string messageText, bool isSilent)
    {
        var urls = StringUtils.MatchUrls(messageText);

        return Download(urls.FirstOrDefault(""), isSilent);
    }
    
    [MessageCommand("Download",
        DefaultGuildUserPermissions = Permissions.AttachFiles,
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel])]
    public async Task InvokeMessage(RestMessage message)
        => await ParseAndDownload(message.Content, false);
    
    [MessageCommand("Download (private)",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel])]
    public async Task InvokeMessage_Private(RestMessage message)
        => await ParseAndDownload(message.Content, true);
}