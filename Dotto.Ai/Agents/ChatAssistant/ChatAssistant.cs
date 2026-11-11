using Dotto.Ai.Agents.Base;
using Dotto.Ai.Prompts;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Agents.ChatAssistant;

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

        // Storage eagerly, container lazily.
        if (options.Enabled)
        {
            await sandbox.InitializeAsync(context.SessionId, context.CallerId, cancellationToken);
            logger.LogInformation("Initialized sandbox metadata for session {SessionId}", context.SessionId);
        }

        try
        {
            var systemPrompt = await BuildSystemPromptAsync(context, options.Enabled, cancellationToken);
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
            // Only on failure: the invoker owns the container.
            context.Attachments.ForEach(att => att.Stream.Dispose());
            context.Attachments.Clear();
            throw;
        }
        finally
        {
            var totalDuration = DateTime.UtcNow - startTime;
            logger.LogInformation(
                "Session {SessionId} finished in {Duration}s",
                context.SessionId, totalDuration.TotalSeconds);
        }
    }

    /// <summary>
    /// Timestamp has one-second resolution; the counter keeps IDs unique within a second.
    /// </summary>
    private static int _sessionCounter;

    private static string GenerateSessionId(ChatAssistantContext context)
    {
        var sequence = Interlocked.Increment(ref _sessionCounter);
        return $"{context.CallerId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{sequence}";
    }

    private async Task<string?> BuildSystemPromptAsync(
        ChatAssistantContext context, bool sandboxEnabled, CancellationToken ct)
    {
        var renderContext = BuildTemplateContext(context, sandboxEnabled);
        return await promptProvider.RenderAsync(DependencyInjection.ChatAssistantPromptKey, renderContext, ct);
    }

    internal static Dictionary<string, object> BuildTemplateContext(
        ChatAssistantContext context, bool sandboxEnabled)
    {
        return new()
        {
            ["CallerName"] = context.CallerName,
            ["CallerId"] = context.CallerId,
            ["ChannelId"] = context.ChannelId,
            ["GuildId"] = context.GuildId,
            ["UtcTime"] = DateTime.UtcNow,
            ["Notes"] = context.Notes,

            ["ToolsEnabled"] = sandboxEnabled
        };
    }
}
