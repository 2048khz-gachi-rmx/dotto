using System.Xml.Linq;
using Dotto.Ai.Agents;
using Dotto.Ai.Models;
using Dotto.Application.Abstractions.Upload;
using Dotto.Common;
using Microsoft.Extensions.AI;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.CommandHandlers.Ai;

public class AiCommandHandler(
    ChatAssistant chatAssistant,
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

        // Process enqueued attachments — upload to Discord or S3 depending on size
        foreach (var att in response.Attachments)
        {
            var length = att.Stream.Length;
            att.Stream.Position = 0;

            if (length <= discordUploadLimit)
            {
                att.DiscordAttachment = new AttachmentProperties(att.FileName.SanitizeHttpHeaderValue(), att.Stream);
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

                att.S3AttachmentUrl = await uploadService.UploadFile(att.Stream, length, att.FileName, contentType, ct);
            }
            else
            {
                // File too large, no S3 — dispose the stream, it'll be silently dropped
                await att.Stream.DisposeAsync();
            }
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
