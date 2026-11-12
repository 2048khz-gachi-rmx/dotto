using System.Xml.Linq;
using Dotto.Ai.Agents;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Dotto.Application.Abstractions.Upload;
using Dotto.Application.Configuration;
using Dotto.Common;
using Dotto.Common.DateTimeProvider;
using Dotto.Domain.Entities;
using Dotto.Infrastructure.Database;
using Microsoft.Extensions.AI;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.CommandHandlers.Ai;

public class AiCommandHandler(
    ChatAssistant chatAssistant,
    ISandbox sandbox,
    GatewayClient gateway,
    IInteractionConfiguration interactionConfiguration,
    DottoDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    IUploadService? uploadService = null)
{
    public async Task<ChatAssistantResponse> Invoke(string userRequest,
        ulong callerId,
        string callerName,
        ulong channelId,
        ulong guildId,
        long discordUploadLimit,
        IList<RestMessage> messages,
        IReadOnlyList<ContextNote>? notes = null,
        AgentProgress? progress = null,
        CancellationToken ct = default)
    {
        if (SelfUserId == null)
            throw new InvalidOperationException("how do we not have self cached?");

        var aiMessages = messages
            .Select(MessageToAiHistoryMessage)
            .Where(m => !(m?.Text).IsNullOrEmpty())
            .ToList();

        var queryXml = BuildSimpleHistoryElement(callerName, userRequest);
        queryXml.Name = "prompt";
        aiMessages.Add(new ChatMessage(ChatRole.User, queryXml.ToString()));

        // Defensive: command modules set the scope before calling in, so this normally no-ops.
        interactionConfiguration.SetScope(
            new InteractionScope(guildId == 0 ? null : guildId, channelId, callerId));

        var settings = await interactionConfiguration.GetCurrentAsync<AiSettings>(ct);

        var context = new ChatAssistantContext
        {
            ChannelId = channelId,
            GuildId = guildId,
            CallerId = callerId,
            CallerName = callerName,
            Messages = aiMessages,
            Notes = notes?.ToList() ?? [],
            Progress = progress ?? new AgentProgress(settings.ChatAssistant.MaxTurns),
        };

        ChatAssistantResponse response;

        // Sandbox outlives materialization; dispose on both paths.
        try
        {
            response = await InvokeAssistant(context, ct);

            var materialization = await MaterializeAttachmentsAsync(
                response, settings.ChatAssistant.MaxAttachments, discordUploadLimit, ct);
            ApplyAttachmentNotes(response, materialization);
        }
        finally
        {
            await sandbox.DisposeAsync();
        }

        return response;
    }

    /// <summary>
    /// Test seam: substitute the assistant invocation.
    /// </summary>
    internal virtual Task<ChatAssistantResponse> InvokeAssistant(
        ChatAssistantContext context, CancellationToken ct)
        => chatAssistant.Invoke(context, ct);

    /// <summary>
    /// Test seam: the bot's own user ID.
    /// </summary>
    internal virtual ulong? SelfUserId => gateway.Cache.User?.Id;

    /// <summary>
    /// Enforces MaxAttachments, then uploads to Discord or S3.
    /// </summary>
    internal async Task<AttachmentMaterializationResult> MaterializeAttachmentsAsync(
        ChatAssistantResponse response, int configuredMaxAttachments, long discordUploadLimit, CancellationToken ct)
    {
        var maxAttachments = Math.Max(0, configuredMaxAttachments);
        var maxUploadSize = Math.Max(discordUploadLimit, uploadService?.MaxFileSizeBytes ?? 0);
        var omittedCount = 0;
        var oversizedCount = 0;

        // Enforce the attachment budget before touching any bytes.
        if (response.Attachments.Count > maxAttachments)
        {
            foreach (var extra in response.Attachments.Skip(maxAttachments))
                await extra.Stream.DisposeAsync();

            omittedCount = response.Attachments.Count - maxAttachments;
            response.Attachments.RemoveRange(maxAttachments, omittedCount);
        }

        // Discord-bound bytes are buffered into bot-owned temp files so the container can stop.
        foreach (var att in response.Attachments)
        {
            var length = att.Length;

            if (length > maxUploadSize)
            {
                await att.Stream.DisposeAsync();
                oversizedCount++;
                continue;
            }

            if (length <= discordUploadLimit)
            {
                var tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"dotto-ai-{Guid.NewGuid():N}{Path.GetExtension(att.FileName)}");

                var temp = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.DeleteOnClose | FileOptions.Asynchronous);

                try
                {
                    await att.Stream.CopyToAsync(temp, ct);
                    temp.Position = 0;
                }
                catch
                {
                    await temp.DisposeAsync();
                    throw;
                }
                finally
                {
                    await att.Stream.DisposeAsync();
                }

                att.DiscordAttachment = new AttachmentProperties(
                    att.FileName.SanitizeHttpHeaderValue(), temp);
            }
            else if (uploadService != null)
            {
                var extension = Path.GetExtension(att.FileName).TrimStart('.');
                var contentType = extension switch
                {
                    "mp4" => "video/mp4",
                    "webm" => "video/webm",
                    "mov" => "video/quicktime",
                    "gif" => "image/gif",
                    "png" => "image/png",
                    "jpg" or "jpeg" => "image/jpeg",
                    "mp3" => "audio/mpeg",
                    _ => "application/octet-stream"
                };

                try
                {
                    att.S3AttachmentUrl = await uploadService.UploadFile(
                        att.Stream, length, att.FileName, contentType, ct);
                }
                finally
                {
                    await att.Stream.DisposeAsync();
                }
            }
            else
            {
                await att.Stream.DisposeAsync();
            }
        }

        return new AttachmentMaterializationResult(
            omittedCount, oversizedCount, maxUploadSize, maxAttachments);
    }

    internal static void ApplyAttachmentNotes(
        ChatAssistantResponse response, AttachmentMaterializationResult materialization)
    {
        var (omittedCount, oversizedCount, maxUploadSize, maxAttachments) = materialization;

        if (omittedCount <= 0 && oversizedCount <= 0)
            return;

        var notes = new List<string>();
        if (omittedCount > 0)
            notes.Add($"{omittedCount} attachment(s) omitted (limit is {maxAttachments} per response).");
        if (oversizedCount > 0)
            notes.Add($"{oversizedCount} attachment(s) omitted (exceeds the {maxUploadSize} byte upload limit).");

        var note = string.Join(" ", notes);
        response.Response = response.Response.IsNullOrEmpty()
            ? note
            : $"{response.Response}\n\n{note}";
    }

    /// <summary>
    /// Records one row per file actually delivered (Discord attachment or S3 link).
    /// </summary>
    public async Task LogUploadedMedia(
        RestMessage sentMessage, ChatAssistantResponse response, ulong invokerUserId)
    {
        if (response.Attachments.IsEmpty() && sentMessage.Attachments.Count == 0)
            return;

        var createdOn = dateTimeProvider.UtcNow;
        var records = new List<DownloadedMediaRecord>();

        // Discord-bound attachments are uploaded in list order, so the Nth sent
        // attachment belongs to the Nth response attachment carrying one.
        var sentAttachments = sentMessage.Attachments.ToList();
        var discordBound = response.Attachments
            .Where(att => att.DiscordAttachment != null)
            .ToList();

        // Be defensive about count mismatches: log whatever pairs up.
        for (var i = 0; i < Math.Min(discordBound.Count, sentAttachments.Count); i++)
        {
            var att = discordBound[i];
            records.Add(new DownloadedMediaRecord
            {
                Id = default,
                ChannelId = sentMessage.ChannelId,
                MessageId = sentMessage.Id,
                DownloadedFrom = att.SourcePath ?? att.FileName,
                InvokerId = invokerUserId,
                MediaUrl = sentAttachments[i].Url,
                CreatedOn = createdOn,
            });
        }

        foreach (var att in response.Attachments)
        {
            if (att.S3AttachmentUrl is null)
                continue;

            records.Add(new DownloadedMediaRecord
            {
                Id = default,
                ChannelId = sentMessage.ChannelId,
                MessageId = sentMessage.Id,
                DownloadedFrom = att.SourcePath ?? att.FileName,
                InvokerId = invokerUserId,
                MediaUrl = att.S3AttachmentUrl.ToString(),
                CreatedOn = createdOn,
            });
        }

        var distinct = records
            .DistinctBy(record => record.MediaUrl)
            .ToList();

        if (distinct.IsEmpty())
            return;

        dbContext.DownloadedMedia.AddRange(distinct);
        await dbContext.SaveChangesAsync();
    }

    internal readonly record struct AttachmentMaterializationResult(
        int OmittedCount, int OversizedCount, long MaxUploadSize, int MaxAttachments);

    private ChatMessage? MessageToAiHistoryMessage(RestMessage arg)
    {
        if (arg.Content.IsNullOrWhitespace() && arg.Attachments.IsNullOrEmpty())
            return null;
            
        var isOwn = arg.Author.Id == SelfUserId!.Value;
        
        var role = isOwn
            ? ChatRole.Assistant
            : ChatRole.User;

        XElement xml;

        // InteractionMetadata doesn't expose the name of the interaction. brilliant
        var commandName = arg.Interaction?.Name;

        // TODO: i think this logic shouldn't be here
        if (!commandName.IsNullOrWhitespace())
        {
            xml = new XElement("command",
                new XAttribute("name", commandName),
                new XAttribute("caller", arg.InteractionMetadata?.User.Username ?? "unknown"),
                new XAttribute("is_you", isOwn));

            var response = arg.Content;

            // hardcode but who cares
            if (isOwn && commandName == "ai")
            {
                // discord doesn't expose the interaction's called arguments (though the client displays it). strange stuff
                // we hope we self-embed it. prompt injections? don't sweat it
                const string prefix = "-# prompt: ";
                var lineBreakIndex = response.IndexOf('\n');
                var sourcePrompt = lineBreakIndex != -1 
                    ? response.Substring(prefix.Length, lineBreakIndex - prefix.Length).TrimEnd('\r')
                    : null;

                if (sourcePrompt != null)
                {
                    xml.Add(new XAttribute("initial_request", sourcePrompt));
                    response = response.Substring(lineBreakIndex + 1);
                }
            }
            
            xml.Add(new XElement("response", response));
        }
        else
        {
            xml = BuildSimpleHistoryElement(arg.Author.Username, arg.Content.Trim());
        }

        if (!arg.Attachments.IsEmpty())
            xml.Add(arg.Attachments.Select(att =>
                new XElement("attachment",
                    new XAttribute("name", att.FileName),
                    new XAttribute("url", att.Url))));

        return new ChatMessage(role, xml.ToString());
    }

    private static XElement BuildSimpleHistoryElement(string username, string content)
    {
        return new XElement("message",
            new XAttribute("user", username),
            new XElement("content", content));
    }
}
