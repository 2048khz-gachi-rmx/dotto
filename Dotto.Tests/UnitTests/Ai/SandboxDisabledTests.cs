using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai;
using Dotto.Ai.Prompts;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Dotto.Application.Abstractions.Factories;
using Dotto.Application.Abstractions.MediaProcessing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Disabled sandbox: no tools, no storage, no reaper.
/// </summary>
public class SandboxDisabledTests
{
    private static (ServiceProvider Provider, string StorageBase, CapturingChatClient ChatClient) BuildProvider(
        bool sandboxEnabled)
    {
        var storageBase = Path.Combine(
            Path.GetTempPath(), "dotto-sandbox-test-" + Guid.NewGuid().ToString("N"));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Sandbox:Enabled"] = sandboxEnabled ? "true" : "false",
                ["Ai:Sandbox:StorageBasePath"] = storageBase
            })
            .Build();

        var chatClient = new CapturingChatClient();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddAi(settings =>
        {
            settings.BaseUrl = new Uri("http://127.0.0.1:1");
            settings.ApiKey = "not-a-real-api-key";
            settings.AssistantModel = "test-model";
            settings.ChatAssistant = new ChatAssistantSettings();
        });

        // Replace both heavyweight externals registered/resolved by the agent
        // factory and the tools: no Docker daemon, no downloader services. The
        // chat client is swapped for one that records the ChatOptions it is
        // called with, so the test observes exactly what the model would see.
        services.AddKeyedSingleton<IChatClient>(Dotto.Ai.DependencyInjection.AssistantKey, chatClient);
        services.AddSingleton(Substitute.For<IDockerClient>());
        services.AddSingleton(Substitute.For<IDownloaderServiceFactory>());
        services.AddSingleton(Substitute.For<IMediaDownloader>());

        return (services.BuildServiceProvider(), storageBase, chatClient);
    }

    private static Sandbox CreateSandbox(bool enabled, string storageBase, out IDockerClient client)
    {
        client = Substitute.For<IDockerClient>();
        var options = Options.Create(new SandboxOptions
        {
            Enabled = enabled,
            StorageBasePath = storageBase
        });

        return new Sandbox(options, NullLogger<Sandbox>.Instance, client);
    }

    [Test]
    public async Task DisabledAgent_ExposesNoTools()
    {
        var (provider, storageBase, chatClient) = BuildProvider(sandboxEnabled: false);
        try
        {
            var agent = provider.GetRequiredKeyedService<AIAgent>(
                Dotto.Ai.DependencyInjection.AssistantKey);

            await agent.RunAsync([new ChatMessage(ChatRole.User, "hello")]);

            // This is exactly what the model is offered: no tools at all.
            (chatClient.LastOptions?.Tools?.Count ?? 0).ShouldBe(0);

            // Resolving the agent (and building its tools) must not provision
            // per-caller storage, even indirectly.
            Directory.Exists(storageBase).ShouldBeFalse();
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Test]
    public async Task EnabledAgent_ExposesSandboxTools()
    {
        var (provider, _, chatClient) = BuildProvider(sandboxEnabled: true);
        try
        {
            provider.GetRequiredService<Dotto.Ai.Agents.Base.ContextAccessor>()
                .Set(new Dotto.Ai.Agents.ChatAssistant.ChatAssistantContext
                {
                    CallerId = 1,
                    CallerName = "tester",
                    ChannelId = 2,
                    GuildId = 3
                });

            var agent = provider.GetRequiredKeyedService<AIAgent>(
                Dotto.Ai.DependencyInjection.AssistantKey);

            await agent.RunAsync([new ChatMessage(ChatRole.User, "hello")]);

            var tools = chatClient.LastOptions?.Tools;
            tools.ShouldNotBeNull();
            tools!.Count.ShouldBeGreaterThan(0);
            tools.Select(t => t.Name).ShouldContain("ExecuteCommand");
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Test]
    public async Task InitializeAsync_WhenDisabled_ThrowsActionableException_WithoutTouchingFilesystem()
    {
        var storageBase = Path.Combine(
            Path.GetTempPath(), "dotto-sandbox-disabled-" + Guid.NewGuid().ToString("N"));
        var sandbox = CreateSandbox(enabled: false, storageBase, out var client);

        try
        {
            var ex = await Should.ThrowAsync<SandboxDisabledException>(
                () => sandbox.InitializeAsync("session", 123, CancellationToken.None));

            ex.ShouldBeAssignableTo<InvalidOperationException>();
            ex.Message.ShouldContain("Ai:Sandbox:Enabled=false");

            Directory.Exists(storageBase).ShouldBeFalse();
            await client.Containers.DidNotReceive()
                .CreateContainerAsync(Arg.Any<CreateContainerParameters>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            await sandbox.DisposeAsync();

            if (Directory.Exists(storageBase))
                Directory.Delete(storageBase, true);
        }
    }

    [Test]
    public async Task EnsureStartedAsync_WhenDisabled_ThrowsActionableException_WithoutCreatingContainer()
    {
        var storageBase = Path.Combine(
            Path.GetTempPath(), "dotto-sandbox-disabled-" + Guid.NewGuid().ToString("N"));
        var sandbox = CreateSandbox(enabled: false, storageBase, out var client);

        try
        {
            var ex = await Should.ThrowAsync<SandboxDisabledException>(
                () => sandbox.EnsureStartedAsync(CancellationToken.None));

            ex.Message.ShouldContain("Ai:Sandbox:Enabled=false");

            await client.Containers.DidNotReceive()
                .CreateContainerAsync(Arg.Any<CreateContainerParameters>(), Arg.Any<CancellationToken>());
            Directory.Exists(storageBase).ShouldBeFalse();
        }
        finally
        {
            await sandbox.DisposeAsync();

            if (Directory.Exists(storageBase))
                Directory.Delete(storageBase, true);
        }
    }

    [Test]
    public async Task InitializeAsync_WhenEnabled_StillProvisionsStorage()
    {
        var storageBase = Path.Combine(
            Path.GetTempPath(), "dotto-sandbox-enabled-" + Guid.NewGuid().ToString("N"));
        var sandbox = CreateSandbox(enabled: true, storageBase, out _);

        try
        {
            var metadata = await sandbox.InitializeAsync("session", 42, CancellationToken.None);

            metadata.CallerId.ShouldBe(42UL);
            Directory.Exists(Path.Combine(storageBase, "42")).ShouldBeTrue();
        }
        finally
        {
            await sandbox.DisposeAsync();

            if (Directory.Exists(storageBase))
                Directory.Delete(storageBase, true);
        }
    }

    [Test]
    public async Task Reaper_WhenDisabled_DoesNotQueryDocker()
    {
        var client = Substitute.For<IDockerClient>();
        var reaper = new SandboxReaper(
            Options.Create(new SandboxOptions { Enabled = false }),
            NullLogger<SandboxReaper>.Instance,
            client);

        await reaper.StartAsync(CancellationToken.None);
        await reaper.StopAsync(CancellationToken.None);

        await client.Containers.DidNotReceive()
            .ListContainersAsync(Arg.Any<ContainersListParameters>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Prompt_OmitsToolInstructions_WhenToolsDisabled()
    {
        var provider = new PromptProvider(Options.Create(new AiSettings
        {
            BaseUrl = new Uri("http://127.0.0.1:1"),
            ApiKey = "not-a-real-api-key",
            AssistantModel = "test-model",
            ChatAssistant = new ChatAssistantSettings(),
            Prompts = new Dictionary<string, PromptSource>
            {
                [Dotto.Ai.DependencyInjection.ChatAssistantPromptKey] = new()
                {
                    Type = PromptType.Resource,
                    Path = "Resources/ChatAssistant.liquid"
                }
            }
        }));

        var enabled = await provider.RenderAsync(
            Dotto.Ai.DependencyInjection.ChatAssistantPromptKey,
            new Dictionary<string, object> { ["ToolsEnabled"] = true, ["UtcTime"] = DateTime.UtcNow },
            CancellationToken.None);

        var disabled = await provider.RenderAsync(
            Dotto.Ai.DependencyInjection.ChatAssistantPromptKey,
            new Dictionary<string, object> { ["ToolsEnabled"] = false, ["UtcTime"] = DateTime.UtcNow },
            CancellationToken.None);

        enabled.ShouldNotBeNull();
        enabled!.ShouldContain("ExecuteCommand");

        disabled.ShouldNotBeNull();
        disabled!.ShouldNotContain("ExecuteCommand");
        disabled.ShouldNotContain("UploadFile");
    }

    [Test]
    public void PromptContext_ToolsEnabledFollowsSandboxOption()
    {
        // The renderer test above passes ToolsEnabled directly; this asserts the
        // flag ChatAssistant derives from IOptions<SandboxOptions>.
        var context = new Dotto.Ai.Agents.ChatAssistant.ChatAssistantContext
        {
            CallerId = 42,
            CallerName = "tester",
            ChannelId = 1,
            GuildId = 2
        };

        ((bool)Dotto.Ai.Agents.ChatAssistant.ChatAssistant
            .BuildTemplateContext(context, sandboxEnabled: true)["ToolsEnabled"])
            .ShouldBeTrue();

        ((bool)Dotto.Ai.Agents.ChatAssistant.ChatAssistant
            .BuildTemplateContext(context, sandboxEnabled: false)["ToolsEnabled"])
            .ShouldBeFalse();
    }

    /// <summary>
    /// Stands in for the model endpoint: records the <see cref="ChatOptions"/>
    /// the agent passes down, which is where the tool list lives.
    /// </summary>
    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Streaming is not used by the assistant.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
