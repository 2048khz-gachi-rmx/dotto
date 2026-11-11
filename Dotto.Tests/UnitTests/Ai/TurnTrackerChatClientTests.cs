using System.Runtime.CompilerServices;
using Dotto.Ai.Agents.Base;
using Microsoft.Extensions.AI;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins turn counting: one turn per model request, so a response that batches several tool
/// calls spends one turn rather than one per call.
/// </summary>
public class TurnTrackerChatClientTests
{
    [Test]
    public async Task EachModelRequestBeginsATurn()
    {
        var progress = new AgentProgress(maxTurns: 20);
        var client = new TurnTrackerChatClient(new StubChatClient(), () => progress);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "again")]);

        progress.Turns.ShouldBe(2);
    }

    [Test]
    public async Task ARunWithoutProgressIsLeftAlone()
    {
        var inner = new StubChatClient();
        var client = new TurnTrackerChatClient(inner, () => null);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        response.Messages.ShouldHaveSingleItem().Text.ShouldBe("answer");
        inner.Requests.ShouldBe(1);
    }

    private sealed class StubChatClient : IChatClient
    {
        public int Requests { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Requests++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "answer");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
