using Dotto.Ai.Prompts;
using Dotto.Ai.Settings;
using Dotto.Application.Configuration;
using Dotto.Tests.Mocks;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

public class PromptProviderTests
{
    private static PromptProvider Build(params (string Key, PromptSource Source)[] prompts)
    {
        var settings = new AiSettings
        {
            BaseUrl = new Uri("http://localhost"),
            ApiKey = "test",
            AssistantModel = "test",
        };

        foreach (var (key, source) in prompts)
            settings.Prompts[key] = source;

        var configuration = new StubInteractionConfiguration().With(settings);
        configuration.SetScope(new InteractionScope(null, 1, 1));

        return new PromptProvider(configuration, new PromptTemplateCache());
    }

    [Test]
    public async Task TextSource_RendersTheInlineTemplate()
    {
        var provider = Build(("Inline", new PromptSource
        {
            Type = PromptType.Text,
            Path = "You are talking to {{CallerName}} in channel {{ChannelId}}."
        }));

        var prompt = await provider.RenderAsync("Inline",
            new Dictionary<string, object> { ["CallerName"] = "alice", ["ChannelId"] = 42 });

        prompt.ShouldBe("You are talking to alice in channel 42.");
    }

    [Test]
    public async Task TextSource_IsNeverTreatedAsAFilePath()
    {
        // Looks like a path and exists nowhere: Text must not touch the filesystem.
        var provider = Build(("Inline", new PromptSource
        {
            Type = PromptType.Text,
            Path = "does/not/exist-{{Name}}.liquid"
        }));

        var prompt = await provider.RenderAsync("Inline",
            new Dictionary<string, object> { ["Name"] = "x" });

        prompt.ShouldBe("does/not/exist-x.liquid");
    }

    [Test]
    public async Task UnregisteredKey_RendersNothing()
    {
        var provider = Build();

        var prompt = await provider.RenderAsync("Missing", new Dictionary<string, object>());

        prompt.ShouldBeNull();
    }

    [Test]
    public async Task LowercaseOverrideOfTheBuiltInPromptStillApplies()
    {
        // Override rows are stored lowercase, so the binder takes the override provider's
        // casing for the dictionary key ("chatassistant") while the code asks for
        // "ChatAssistant". This used to silently drop the whole system prompt.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:BaseUrl"] = "http://localhost",
                ["Ai:ApiKey"] = "test",
                ["Ai:AssistantModel"] = "test",
                ["Ai:Prompts:ChatAssistant:Type"] = "Resource",
                ["Ai:Prompts:ChatAssistant:Path"] = "ChatAssistant.liquid"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ai:prompts:chatassistant:type"] = "Text",
                ["ai:prompts:chatassistant:path"] = "You speak like a pirate."
            })
            .Build();

        var settings = configuration.GetSection("Ai").Get<AiSettings>()!;

        var stub = new StubInteractionConfiguration().With(settings);
        stub.SetScope(new InteractionScope(null, 1, 1));

        var provider = new PromptProvider(stub, new PromptTemplateCache());
        var prompt = await provider.RenderAsync(
            "ChatAssistant", new Dictionary<string, object> { ["ToolsEnabled"] = true });

        prompt.ShouldBe("You speak like a pirate.");
    }
}
