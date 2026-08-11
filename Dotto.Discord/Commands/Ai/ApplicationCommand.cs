using Dotto.Ai.Settings;
using Dotto.Common;
using Dotto.Common.DateTimeProvider;
using Dotto.Discord.CommandHandlers.Ai;
using Dotto.Discord.CommandHandlers.Download;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Dotto.Discord.Commands.Ai;

public class ApplicationCommand(IServiceProvider serviceProvider, IDateTimeProvider dateTimeProvider)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly AiCommandHandler _aiHandler = serviceProvider.GetRequiredService<AiCommandHandler>();
    private readonly AiSettings _settings = serviceProvider.GetRequiredService<IOptions<AiSettings>>().Value;

    private async Task Invoke(string userRequest)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var messageHistory = await Context.Channel.GetMessagesAsync(new()
            {
                BatchSize = Math.Min(_settings.ChatAssistant.HistoryContextSize, 100),
                Direction = PaginationDirection.Before,
                From = Context.Interaction.Id
            })
            .Take(_settings.ChatAssistant.HistoryContextSize)
            .OrderBy(msg => msg.CreatedAt)
            .ToListAsync();

        var now = dateTimeProvider.UtcNow;

        messageHistory = messageHistory
            .Where(msg => (now - msg.CreatedAt) < _settings.ChatAssistant.HistoryMaxAge)
            .ToList();

        var uploadLimit = DownloadCommandHandler.GetMaxDiscordFileSize(Context.Guild, Context.User);

        var response = await _aiHandler.Invoke(userRequest,
            Context.User.Id,
            Context.User.Username,
            Context.Channel.Id,
            Context.Guild?.Id ?? 0,
            uploadLimit,
            messageHistory);

        // Workaround: you can't get past interactions' options, so we embed the prompt to keep history
        var textResponse = $"-# prompt: {userRequest.Replace("\n", "")}\n"
                           + response.Response;
        var s3Atts = response.Attachments
            .Select(att => att.S3AttachmentUrl)
            .Where(att => att != null)
            .ToList();

        if (!s3Atts.IsNullOrEmpty())
            textResponse += "\n" + string.Join("\n", s3Atts.Select(url => "-# " + url));

        await FollowupAsync(new()
        {
            Content = textResponse,
            Attachments = response.Attachments.Select(att => att.DiscordAttachment).Where(att => att != null)!
        });
    }

    [SlashCommand("ai", "Chat with the AI assistant",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel],
        DefaultGuildPermissions = Permissions.SendMessages)]
    public Task InvokeSlash(
        [SlashCommandParameter(Name = "prompt", Description = "What you want to ask the assistant")]
        string userRequest)
        => Invoke(userRequest);

    [MessageCommand("Ask AI",
        Contexts = [InteractionContextType.Guild, InteractionContextType.DMChannel, InteractionContextType.BotDMChannel])]
    public async Task InvokeMessage(RestMessage message)
        => await Invoke(message.Content);
}