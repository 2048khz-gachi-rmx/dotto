using Dotto.Ai;
using Dotto.Application;
using Dotto.Application.Configuration;
using Dotto.Common.Configuration;
using Dotto.Domain.Entities;
using Dotto.Discord;
using Dotto.Ffmpeg;
using Dotto.Infrastructure.Downloader;
using Dotto.Infrastructure.FileUpload;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Dotto.Tests.UnitTests.Configuration;

public class ConfigurationSchemaRegistrationTests
{
    [Test]
    public void EveryModuleRegistersItsOptionsTypeWithTheExpectedSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:BaseUrl"] = "https://example/v1",
                ["Ai:ApiKey"] = "key",
                ["Ai:AssistantModel"] = "model",
                ["Minio:AccessKey"] = "test",
                ["Minio:SecretKey"] = "test",
                ["Minio:BucketName"] = "test",
                ["Minio:Region"] = "us-east-1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddApplication();
        services.AddAi(settings =>
        {
            settings.BaseUrl = new Uri("https://example/v1");
            settings.ApiKey = "key";
            settings.AssistantModel = "model";
        });
        services.AddDownloader(_ => { });
        services.AddFfmpeg();
        services.AddFileUploader();
        services.AddDiscordIntegration(configuration.GetSection("Discord"));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConfigurationSchemaRegistry>();

        var sections = registry.Schemas.ToDictionary(schema => schema.Section, schema => schema.OptionsType);
        sections["Ai"].ShouldBe(typeof(Dotto.Ai.Settings.AiSettings));
        sections["Ai:Sandbox"].ShouldBe(typeof(Dotto.Ai.Settings.SandboxOptions));
        sections["Downloader"].ShouldBe(typeof(Dotto.Infrastructure.Downloader.Settings.DownloaderSettings));
        sections["Compress"].ShouldBe(typeof(Dotto.Ffmpeg.Settings.CompressionSettings));
        sections["Minio"].ShouldBe(typeof(Dotto.Infrastructure.FileUpload.S3Settings));
        sections["Discord:AutoDownload"].ShouldBe(typeof(Dotto.Discord.Settings.AutoDownloadSettings));
        sections["UrlCorrection"].ShouldBe(typeof(Dotto.Application.Settings.UrlCorrectionSettings));
    }

    [Test]
    public void KnownKeys_ContainNestedScalarLeaves()
    {
        var registry = new ConfigurationSchemaRegistry(
        [
            new ConfigurationOptionSchema("Ai", typeof(Dotto.Ai.Settings.AiSettings)),
            new ConfigurationOptionSchema("Ai:Sandbox", typeof(Dotto.Ai.Settings.SandboxOptions))
        ]);

        var keys = registry.GetKnownKeys();

        keys.ShouldContain("Ai:ChatAssistant:Model");
        keys.ShouldContain("Ai:Sandbox:MemoryMb");
        keys.ShouldNotContain("Ai:Prompts");
    }

    [Test]
    public void ScopeSupport_FollowsTheNearestDeclaration()
    {
        var registry = new ConfigurationSchemaRegistry(
        [
            new ConfigurationOptionSchema("Ai", typeof(Dotto.Ai.Settings.AiSettings)),
            new ConfigurationOptionSchema("Ai:Sandbox", typeof(Dotto.Ai.Settings.SandboxOptions))
        ]);

        registry.GetScopeSupport("Ai:ChatAssistant:Model").ShouldBe(ConfigurationScopeSupport.Any);
        registry.GetScopeSupport("Ai:Prompts:MyPrompt:Path").ShouldBe(ConfigurationScopeSupport.Any);
        registry.GetScopeSupport("Ai:Sandbox:MemoryMb").ShouldBe(ConfigurationScopeSupport.Global);
        registry.GetScopeSupport("NotRegistered:Key").ShouldBe(ConfigurationScopeSupport.Global);
    }

    [Test]
    public void KnownKeys_CanBeLimitedToScopesThatResolvePerInteraction()
    {
        var registry = new ConfigurationSchemaRegistry(
        [
            new ConfigurationOptionSchema("Ai", typeof(Dotto.Ai.Settings.AiSettings)),
            new ConfigurationOptionSchema("Ai:Sandbox", typeof(Dotto.Ai.Settings.SandboxOptions))
        ]);

        registry.GetKnownKeys(ConfigurationScope.Guild).ShouldContain("Ai:ChatAssistant:Model");
        registry.GetKnownKeys(ConfigurationScope.Guild).ShouldNotContain("Ai:Sandbox:MemoryMb");
        registry.GetKnownKeys().ShouldContain("Ai:Sandbox:MemoryMb");
    }
}
