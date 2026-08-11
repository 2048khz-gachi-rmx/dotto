using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Settings;
using Dotto.Common;
using Dotto.Common.DateTimeProvider;
using Dotto.Discord.CommandHandlers.Ai;
using Dotto.Discord.CommandHandlers.Download;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace Dotto.Discord.Commands.Ai;

public class TextCommand(
    IServiceProvider serviceProvider,
    IDateTimeProvider dateTimeProvider,
    RestClient client) : CommandModule<CommandContext>
{
    private readonly AiCommandHandler _aiHandler = serviceProvider.GetRequiredService<AiCommandHandler>();
    private readonly AiSettings _settings = serviceProvider.GetRequiredService<IOptions<AiSettings>>().Value;

    [Command("ai")]
    public async Task Invoke([CommandParameter(Remainder = true)] string userRequest)
    {
        if (Context.Message.GuildId == null)
            return;
        
        var channelId = Context.Message.ChannelId;
        var guildId = Context.Message.GuildId.Value;
        
        // Fetch messages before the invoking message; skip the user's own message so the
        // query (passed separately as userRequest) isn't duplicated in history.
        var messageHistory = await client.GetMessagesAsync(channelId, new()
            {
                BatchSize = Math.Min(_settings.ChatAssistant.HistoryContextSize, 100),
                Direction = PaginationDirection.Before,
                From = Context.Message.Id
            })
            .Take(_settings.ChatAssistant.HistoryContextSize)
            .Where(msg => msg.Id != Context.Message.Id)
            .OrderBy(msg => msg.CreatedAt)
            .ToListAsync();

        var now = dateTimeProvider.UtcNow;

        messageHistory = messageHistory
            .Where(msg => (now - msg.CreatedAt) < _settings.ChatAssistant.HistoryMaxAge)
            .ToList();

        var uploadLimit = DownloadCommandHandler.GetMaxDiscordFileSize(Context.Guild, Context.User);

        var typingTask = client.EnterTypingScopeAsync(Context.Message.ChannelId).AsTask();
        ChatAssistantResponse response;
        try
        {
            response = await _aiHandler.Invoke(userRequest,
                Context.User.Id,
                Context.User.Username,
                channelId,
                guildId,
                uploadLimit,
                messageHistory);
        }
        finally
        {
            _ = typingTask.ContinueWith(task => task.Result.Dispose());
        }

        var textResponse = response.Response ?? "";
        var s3Atts = response.Attachments
            .Select(att => att.S3AttachmentUrl)
            .Where(att => att != null)
            .ToList();

        if (!s3Atts.IsNullOrEmpty())
            textResponse += "\n" + string.Join("\n", s3Atts.Select(url => "-# " + url));

        await ReplyAsync(new ReplyMessageProperties
        {
            Content = textResponse,
            Attachments = response.Attachments.Select(att => att.DiscordAttachment).Where(att => att != null)!
        });
    }
}
