using Dotto.Common;
using Dotto.Discord.CommandHandlers.Download;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Commands.Download;

public class ApplicationCommand(DownloadCommand dl) : ApplicationCommandModule<ApplicationCommandContext>
{
    private async Task Download(string uriString, bool isSilent, bool audioOnly)
    {
        var flags = isSilent ? MessageFlags.Ephemeral : default;
        
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            await RespondAsync(InteractionCallback.Message(new() { Content = "No URL matched", Flags = flags }));
            return;
        }

        // if an unhandled exception occurs, NetCord will acknowledge the command with an error instead of following up,
        // which will give us a 400 Bad Request by discord. so let's check for synchronous errors first
        var uploadLimit = Context.Interaction.AttachmentSizeLimit;
        var hydrateTask = dl.CreateMessage<InteractionMessageProperties>(uri, audioOnly, uploadLimit);
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

    [SlashCommand("dl", "Download from URL via yt-dlp",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel],
        DefaultGuildPermissions = Permissions.AttachFiles | Permissions.SendMessages | Permissions.EmbedLinks)]
    public Task InvokeSlash(
        [SlashCommandParameter(Name = "url", Description = "Link to the media you want to download")]
        string uriString,
        [SlashCommandParameter(Name = "private", Description = "Should the downloaded audio be hidden from others?")]
        bool isSilent = false,
        [SlashCommandParameter(Name = "audio_only", Description = "Only download the audio?")]
        bool audioOnly = false)
        => Download(uriString, isSilent, audioOnly);

    private Task ParseAndDownload(string messageText, bool isSilent)
    {
        var urls = StringUtils.MatchUrls(messageText);

        return Download(urls.FirstOrDefault(""), isSilent, audioOnly: false);
    }
    
    [MessageCommand("Download",
        DefaultGuildPermissions = Permissions.AttachFiles,
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel])]
    public async Task InvokeMessage(RestMessage message)
        => await ParseAndDownload(message.Content, false);
    
    [MessageCommand("Download (private)",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel])]
    public async Task InvokeMessage_Private(RestMessage message)
        => await ParseAndDownload(message.Content, true);
}