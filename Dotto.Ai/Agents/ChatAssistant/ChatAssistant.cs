using Dotto.Ai.Agents.Base;
using Dotto.Ai.Prompts;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Dotto.Application.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Ai.Agents.ChatAssistant;

public class ChatAssistant(
    ContextAccessor accessor,
    IInteractionConfiguration configuration,
    IOptions<SandboxOptions> sandboxOptions,
    ILogger<ChatAssistant> logger,
    IPromptProvider promptProvider,
    ISandbox sandbox,
    IAiAgentFactory agentFactory)
{
    private const string NoAnswerText =
        "The run stopped before producing an answer — it ran out of turns. Try a narrower request.";

    public async Task<ChatAssistantResponse> Invoke(
        ChatAssistantContext context, CancellationToken cancellationToken)
    {
        // The boundary already set this interaction's scope, so this is the same cached
        // snapshot the command handler resolved (no second query, no divergent values).
        var settings = await configuration.GetCurrentAsync<AiSettings>(cancellationToken);

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

            var agent = agentFactory.CreateAgent(settings);
            var response = await agent.RunAsync(
                context.Messages,
                cancellationToken: cancellationToken);

            var finalMessage = response.Messages.Last();
            var text = finalMessage.Text;

            // A run can only end this way if the framework stopped invoking tools mid-call (its
            // iteration cap) or a provider returned a call without content. Say so instead of
            // handing the user an empty message or a leaked tool call.
            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning(
                    "Session {SessionId} ended without answer text (final contents: {Contents})",
                    context.SessionId,
                    string.Join(", ", finalMessage.Contents.Select(content => content.GetType().Name)));
                text = NoAnswerText;
            }

            var assistantResponse = new ChatAssistantResponse()
            {
                Response = text,
                Attachments = context.Attachments
            };

            var duration = DateTime.UtcNow - startTime;
            assistantResponse.Duration = duration;
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
        var prompt = await promptProvider.RenderAsync(
            DependencyInjection.ChatAssistantPromptKey, renderContext, ct);

        if (prompt == null)
        {
            // Running without instructions is rarely intended, and a casing or key-name
            // mismatch used to be completely silent.
            logger.LogWarning(
                "No '{PromptKey}' prompt is configured under Ai:Prompts; running without a system prompt.",
                DependencyInjection.ChatAssistantPromptKey);
        }

        return prompt;
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
