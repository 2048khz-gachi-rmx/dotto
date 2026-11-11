using Microsoft.Extensions.AI;

namespace Dotto.Ai.Agents.Base;

/// <summary>
/// Counts model turns for the current run: one turn is one request to the model, however many
/// tools that response asks for. The turn budget is spent per turn, so a model that batches
/// several calls into one response is not penalised for it.
/// </summary>
internal sealed class TurnTrackerChatClient(IChatClient inner, Func<AgentProgress?> progress)
    : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        progress()?.BeginTurn();
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        progress()?.BeginTurn();
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }
}
