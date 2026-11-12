using System.Runtime.CompilerServices;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins the shape of a session: the tool list and the system prompt are decided once, when the
/// run starts, and every request the model sees carries them unchanged. The turn budget is
/// enforced by denying calls, never by taking tools away — a model that can no longer call a tool
/// tends to write the call into its answer instead.
/// </summary>
public class AgentSessionShapeTests
{
    [Test]
    public async Task EveryRequest_CarriesTheSameToolList_EvenPastTheTurnLimit()
    {
        var (client, progress, response) = await RunScripted(toolCallingTurns: 3, maxTurns: 1);

        client.Requests.Count.ShouldBe(4); // three tool-calling turns plus the answer
        client.ToolListSizes.ShouldAllBe(size => size == 1);
        client.ToolModes.ShouldNotContain(ChatToolMode.None);

        progress.RefusedCalls.ShouldBe(2);
        response.Text.ShouldBe("done");
    }

    [Test]
    public async Task ManyTurns_NeverHitTheFrameworkIterationCap()
    {
        // The framework stops advertising tools on its final iteration; with its default cap (40)
        // that alone would strip the tool list mid-run and hand the unanswered call back.
        var (client, progress, response) = await RunScripted(toolCallingTurns: 50, maxTurns: 100);

        client.ToolListSizes.ShouldAllBe(size => size == 1);
        progress.RefusedCalls.ShouldBe(0);
        response.Text.ShouldBe("done");
    }

    [Test]
    public async Task EveryRequest_CarriesTheSameSystemPrompt_InTheSamePlace()
    {
        var (client, _, _) = await RunScripted(toolCallingTurns: 2, maxTurns: 1);

        client.SystemPrompts.Count.ShouldBe(client.Requests.Count);
        client.SystemPrompts.ShouldAllBe(prompt => prompt!.Text == "system prompt");
        client.SystemPromptPositions.ShouldAllBe(position => position == 0);
    }

    private static async Task<(ScriptedChatClient Client, AgentProgress Progress, AgentResponse Response)>
        RunScripted(int toolCallingTurns, int maxTurns)
    {
        var progress = new AgentProgress(maxTurns);

        var tool = AIFunctionFactory.Create(
            (string command) => new TextToolResult($"ran {command}"),
            new AIFunctionFactoryOptions
            {
                Name = "ExecuteCommand",
                MarshalResult = (value, _, _) => ValueTask.FromResult(value),
                ExcludeResultSchema = true
            });

        var client = new ScriptedChatClient(toolCallingTurns);
        var agent = Dotto.Ai.DependencyInjection.BuildAgent(
            client,
            [new TurnLimitedAIFunction(tool, () => progress)],
            () => progress,
            maxTurns);

        var response = await agent.RunAsync(new List<ChatMessage>
        {
            new(ChatRole.System, "system prompt"),
            new(ChatRole.User, "go")
        });

        return (client, progress, response);
    }

    /// <summary>
    /// Stands in for the model endpoint: asks for a tool call for the first N requests, then
    /// answers. Records what each request looked like on the wire.
    /// </summary>
    private sealed class ScriptedChatClient(int toolCallingTurns) : IChatClient
    {
        public List<IList<AITool>?> ToolLists { get; } = [];

        public List<ChatToolMode?> ToolModes { get; } = [];

        public List<ChatMessage?> SystemPrompts { get; } = [];

        public List<int> SystemPromptPositions { get; } = [];

        public IReadOnlyList<IList<AITool>?> Requests => ToolLists;

        public IEnumerable<int> ToolListSizes => ToolLists.Select(tools => tools?.Count ?? 0);

        private int _requests;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var request = messages.ToList();
            var systemPrompt = request.FirstOrDefault(m => m.Role == ChatRole.System);

            ToolLists.Add(options?.Tools);
            ToolModes.Add(options?.ToolMode);
            SystemPrompts.Add(systemPrompt);
            SystemPromptPositions.Add(systemPrompt is null ? -1 : request.IndexOf(systemPrompt));

            var turn = ++_requests;
            var message = turn <= toolCallingTurns
                ? new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        $"call_{turn}",
                        "ExecuteCommand",
                        new Dictionary<string, object?> { ["command"] = $"cmd {turn}" })
                ])
                : new ChatMessage(ChatRole.Assistant, "done");

            return Task.FromResult(new ChatResponse(message));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
