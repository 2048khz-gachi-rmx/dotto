using Dotto.Discord.CommandHandlers.Compress;
using Dotto.Ffmpeg.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Commands.Compress;

public class ApplicationCommand(IServiceProvider serviceProvider) : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly CompressCommandHandler _compressHandler = serviceProvider.GetRequiredService<CompressCommandHandler>();

    private async Task Compress(Attachment attachment, CompressionMethod format)
    {
        var videos = new List<(Uri Url, string Name)> { (new Uri(attachment.Url), attachment.FileName) };
        var hydrateTask = _compressHandler.CreateMessage<InteractionMessageProperties>(videos, format, false);
        if (hydrateTask.IsFaulted)
        {
            throw hydrateTask.Exception;
        }

        var respTask = RespondAsync(InteractionCallback.DeferredMessage());

        await respTask;

        var response = await hydrateTask;
        await FollowupAsync(response.Message);
    }

    private async Task CompressMessage(RestMessage message, bool isPrivate)
    {
        var flags = isPrivate ? MessageFlags.Ephemeral : default;

        var videos = message.Attachments
            .Where(a => a.ContentType?.StartsWith("video/") == true)
            .Select(a => (new Uri(a.Url), a.FileName))
            .ToList();

        if (videos.Count == 0)
        {
            await RespondAsync(InteractionCallback.Message(new() { Content = "No video attachments found", Flags = flags }));
            return;
        }

        var hydrateTask = _compressHandler.CreateMessage<InteractionMessageProperties>(videos, CompressionMethod.Vp9, false);
        if (hydrateTask.IsFaulted)
        {
            throw hydrateTask.Exception;
        }

        var respTask = RespondAsync(InteractionCallback.DeferredMessage(flags));

        await respTask;

        var response = await hydrateTask;
        await FollowupAsync(response.Message);
    }

    [SlashCommand("compress", "Compress a video attachment using FFmpeg",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel],
        DefaultGuildPermissions = Permissions.AttachFiles | Permissions.SendMessages)]
    public Task InvokeSlash(
        [SlashCommandParameter(Name = "attachment", Description = "Video file to compress")]
        Attachment attachment,
        [SlashCommandParameter(Name = "format", Description = "Compression format (vp9 or av1)")]
        string? format = "vp9")
        => Compress(attachment, format == "av1" ? CompressionMethod.Av1 : CompressionMethod.Vp9);

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
