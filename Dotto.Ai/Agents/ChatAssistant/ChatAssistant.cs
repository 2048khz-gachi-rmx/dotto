using Dotto.Ai.Abstractions;
using Dotto.Ai.Internal;
using Dotto.Ai.Models;
using Dotto.Ai.Settings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Agents;

public class ChatAssistant(
    ContextAccessor accessor,
    IOptions<SandboxOptions> sandboxOptions,
    ILogger<ChatAssistant> logger,
    IPromptProvider promptProvider,
    ISandbox sandbox,
    IServiceProvider serviceProvider)
{
    public async Task<ChatAssistantResponse> Invoke(ChatAssistantContext context, CancellationToken cancellationToken)
    {
        context.SessionId = GenerateSessionId(context);

        accessor.Set(context);

        var options = sandboxOptions.Value;

        var startTime = DateTime.UtcNow;
        logger.LogInformation(
            "Session {SessionId} started (caller: {CallerId}, channel: {ChannelId}, guild: {GuildId})",
            context.SessionId, context.CallerId, context.ChannelId, context.GuildId);

        // Create the host session directory up front (and the rest of the sandbox lifecycle)
        // via the scoped sandbox entity, so tools that depend on the filesystem (DownloadMedia, UploadFile) can use it.
        // The sandbox container itself is started lazily on first container use (like by TerminalTools).
        if (options.Enabled)
        {
            await sandbox.InitializeAsync(context.SessionId, cancellationToken);
            logger.LogInformation("Initialized sandbox metadata for session {SessionId}", context.SessionId);
        }

        try
        {
            var systemPrompt = await BuildSystemPromptAsync(context, cancellationToken);
            if (systemPrompt != null)
                context.Messages.Insert(0, new ChatMessage(ChatRole.System, systemPrompt));

            var agent = serviceProvider.GetRequiredKeyedService<AIAgent>(DependencyInjection.AssistantKey);
            var response = await agent.RunAsync(
                context.Messages,
                cancellationToken: cancellationToken);

            var assistantResponse = new ChatAssistantResponse()
            {
                Response = response.Messages.Last().Text,
                Attachments = context.Attachments
            };

            var duration = DateTime.UtcNow - startTime;
            logger.LogInformation(
                "Session {SessionId} completed in {Duration}s",
                context.SessionId, duration.TotalSeconds);

            return assistantResponse;
        }
        catch
        {
            // clean up attachment streams (if any) only if we errored and won't pass them upwards
            context.Attachments.ForEach(att => att.Stream.Dispose());
            throw;
        }
        finally
        {
            if (options.Enabled)
                await sandbox.DisposeAsync();

            var totalDuration = DateTime.UtcNow - startTime;
            logger.LogInformation(
                "Session {SessionId} torn down after {Duration}s",
                context.SessionId, totalDuration.TotalSeconds);
        }
    }

    private static string GenerateSessionId(ChatAssistantContext context)
    {
        return $"{context.CallerId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
    }

    private async Task<string?> BuildSystemPromptAsync(
        ChatAssistantContext context, CancellationToken ct)
    {
        var renderContext = BuildTemplateContext(context);
        return await promptProvider.RenderAsync(DependencyInjection.ChatAssistantPromptKey, renderContext, ct);
    }

    private static Dictionary<string, object> BuildTemplateContext(ChatAssistantContext context)
    {
        return new()
        {
            ["CallerName"] = context.CallerName,
            ["CallerId"] = context.CallerId,
            ["ChannelId"] = context.ChannelId,
            ["GuildId"] = context.GuildId,
            ["UtcTime"] = DateTime.UtcNow
        };
    }
}
