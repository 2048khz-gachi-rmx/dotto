using System.Text.RegularExpressions;
using Dotto.Common.Constants;
using Dotto.Discord.CommandHandlers.Compress;
using Dotto.Ffmpeg.Contracts;
using Dotto.Ffmpeg.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Commands.Compress;

public class ApplicationCommand(IServiceProvider serviceProvider,
    IOptions<CompressionSettings> compressionSettings)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly CompressCommandHandler _compressHandler = serviceProvider.GetRequiredService<CompressCommandHandler>();
    private readonly CompressionMethod _defaultMethod = compressionSettings.Value.DefaultStrategy;
    
    private async Task Compress(Attachment attachment, CompressionMethod format)
    {
        var videos = new List<(Uri Url, string Name)> { (new Uri(attachment.Url), attachment.FileName) };
        var hydrateTask = _compressHandler.CreateMessage<InteractionMessageProperties>(videos, format, false);
        if (hydrateTask.IsFaulted)
        {
            throw hydrateTask.Exception;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        var response = await hydrateTask;
        await FollowupAsync(response.Message);
    }
    
    private async Task CompressMessage(RestMessage message, bool isPrivate)
    {
        var flags = isPrivate ? MessageFlags.Ephemeral : default;

        var videosToProcess = message.Attachments
            .Where(att => att.ContentType?.StartsWith("video/") == true)
            .Select(att => (new Uri(att.Url), att.Title ?? att.FileName))
            .ToList();

        if (!string.IsNullOrEmpty(message.Content))
        {
            foreach (Match match in Constants.Compression.Regexes.DiscordCdn.Matches(message.Content))
            {
                var url = match.Value;
                var fn = match.Groups[2].Value.ToLower();
                
                if (!Constants.Compression.Regexes.VideoExts.IsMatch(fn))
                    continue;

                videosToProcess.Add((new Uri(url), fn));
            }
        }

        var hydrateTask = _compressHandler.CreateMessage<InteractionMessageProperties>(videosToProcess, _defaultMethod, false);
        if (hydrateTask.IsFaulted)
        {
            throw hydrateTask.Exception;
        }
        
        await RespondAsync(InteractionCallback.DeferredMessage(flags));
        
        var result = await hydrateTask;

        var newMessage = result.HasAnyMedia
            ? result.Message.WithAllowedMentions(AllowedMentionsProperties.None)
            : new InteractionMessageProperties().WithContent("-# no media found...");
        
        await FollowupAsync(newMessage);
    }

    [SlashCommand("compress", "Compress a video attachment using FFmpeg",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel],
        DefaultGuildPermissions = Permissions.AttachFiles | Permissions.SendMessages)]
    public Task InvokeSlash(
        [SlashCommandParameter(Name = "attachment", Description = "Video file to compress")]
        Attachment attachment,
        [SlashCommandParameter(Name = "format", Description = "Compression format")]
        CompressionMethod format = CompressionMethod.Av1)
        => Compress(attachment, format);

    [MessageCommand("Compress",
        DefaultGuildPermissions = Permissions.AttachFiles,
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel])]
    public Task InvokeCompress(RestMessage message)
        => CompressMessage(message, false);

    [MessageCommand("Compress (private)",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel])]
    public Task InvokeCompressPrivate(RestMessage message)
        => CompressMessage(message, true);
}
