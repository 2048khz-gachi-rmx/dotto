using Dotto.Ai.Prompts;
using Dotto.Ai.Settings;
using Dotto.Application.Configuration;
using Dotto.Tests.Mocks;
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
}
