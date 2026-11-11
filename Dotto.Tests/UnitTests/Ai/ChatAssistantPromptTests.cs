using Dotto.Ai.Agents.Base;
using Dotto.Ai.Agents.ChatAssistant;
using Dotto.Ai.Prompts;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins how injected context notes surface in the system prompt.
/// </summary>
public class ChatAssistantPromptTests
{
    private const string NotesHeader = "Additional context for this interaction:";

    private static ChatAssistantContext CreateContext(params ContextNote[] notes) => new()
    {
        CallerId = 1,
        CallerName = "caller",
        ChannelId = 2,
        GuildId = 0,
        Notes = [.. notes],
    };

    private static async Task<string> RenderAsync(ChatAssistantContext context, bool sandboxEnabled = false)
    {
        var settings = new AiSettings
        {
            BaseUrl = new Uri("http://localhost"),
            ApiKey = "test",
            AssistantModel = "test",
            Prompts =
            {
                ["ChatAssistant"] = new PromptSource { Type = PromptType.Resource, Path = "ChatAssistant.liquid" }
            },
        };

        var provider = new PromptProvider(Options.Create(settings));
        var renderContext = ChatAssistant.BuildTemplateContext(context, sandboxEnabled);

        var prompt = await provider.RenderAsync("ChatAssistant", renderContext);
        prompt.ShouldNotBeNull();
        return prompt;
    }

    [Test]
    public async Task Render_WithNote_RendersTitleAndBody()
    {
        var prompt = await RenderAsync(CreateContext(
            new ContextNote("Message history", "Direct messages have no history.")));

        prompt.ShouldContain(NotesHeader);
        prompt.ShouldContain("- Message history: Direct messages have no history.");
    }

    [Test]
    public async Task Render_WithoutNotes_OmitsTheSection()
    {
        var prompt = await RenderAsync(CreateContext());

        prompt.ShouldNotContain(NotesHeader);
    }

    [Test]
    public async Task Render_WithToolsEnabled_DisclosesTheTurnBudget()
    {
        var prompt = await RenderAsync(CreateContext(), sandboxEnabled: true);

        prompt.ShouldContain("limited number of tool calls");
        prompt.ShouldContain("approaching the limit");
        prompt.ShouldContain("last turn");
    }
}
