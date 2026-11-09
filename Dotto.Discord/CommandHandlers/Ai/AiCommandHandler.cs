using System.Xml.Linq;
using Dotto.Ai.Agents;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Sandbox;
using Dotto.Application.Abstractions.Upload;
using Dotto.Common;
using Microsoft.Extensions.AI;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.CommandHandlers.Ai;

public class AiCommandHandler(
    ChatAssistant chatAssistant,
    ISandbox sandbox,
    GatewayClient gateway,
    IUploadService? uploadService = null)
{
    public async Task<ChatAssistantResponse> Invoke(string userRequest,
        ulong callerId,
        string callerName,
        ulong channelId,
        ulong guildId,
        long discordUploadLimit,
        IList<RestMessage> messages,
        CancellationToken ct = default)
    {
        if (gateway.Cache.User == null)
            throw new InvalidOperationException("how do we not have self cached?");

        var aiMessages = messages
            .Select(MessageToAiHistoryMessage)
            .Where(m => !(m?.Text).IsNullOrEmpty())
            .ToList();

        var queryXml = BuildSimpleHistoryElement(callerName, userRequest);
        queryXml.Name = "prompt";
        aiMessages.Add(new ChatMessage(ChatRole.User, queryXml.ToString()));

        var context = new ChatAssistantContext
        {
            ChannelId = channelId,
            GuildId = guildId,
            CallerId = callerId,
            CallerName = callerName,
            Messages = aiMessages,
        };

        var response = await chatAssistant.Invoke(context, ct);

        try
        {
            // Process enqueued attachments — upload to Discord or S3 depending on size.
            //
            // Attachments stream out of the sandbox over HTTP, so their bytes are
            // only readable while the container is alive. S3 uploads happen inline,
            // but Discord uploads happen after this method returns — buffer those
            // into a bot-owned temp file (random name, never derived from sandbox
            // input) so the container can be stopped immediately.
            foreach (var att in response.Attachments)
            {
                var length = att.Length;

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
                    // File too large, no S3 — dispose the stream, it'll be silently dropped
                    await att.Stream.DisposeAsync();
                }
            }
        }
        finally
        {
            // Every attachment has been materialized (S3) or buffered into a
            // bot-owned temp stream (Discord), so the container can stop now.
            // Dispose is idempotent and DI scope disposal may also call it.
            await sandbox.DisposeAsync();
        }

        return response;
    }

    private ChatMessage? MessageToAiHistoryMessage(RestMessage arg)
    {
        if (arg.Content.IsNullOrWhitespace() && arg.Attachments.IsNullOrEmpty())
            return null;
            
        var isOwn = arg.Author.Id == gateway.Cache.User!.Id;
        
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
