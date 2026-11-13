using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>The probe must report a missing runsc without taking startup down.</summary>
public class SandboxRuntimeProbeTests
{
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static IDockerClient ClientWithRuntimes(params string[] runtimes)
    {
        var client = Substitute.For<IDockerClient>();
        client.System
            .GetSystemInfoAsync(Arg.Any<CancellationToken>())
            .Returns(new SystemInfoResponse
            {
                Runtimes = runtimes.ToDictionary(runtime => runtime, _ => new RuntimeWithStatus())
            });
        return client;
    }

    private static (SandboxRuntimeProbe Probe, RecordingLogger<SandboxRuntimeProbe> Logger) Create(
        SandboxOptions options,
        IDockerClient client)
    {
        var logger = new RecordingLogger<SandboxRuntimeProbe>();
        return (new SandboxRuntimeProbe(Options.Create(options), logger, client), logger);
    }

    [Test]
    public async Task StartAsync_WhenRunscIsRegistered_LogsNoError()
    {
        var client = ClientWithRuntimes("runc", "runsc");
        var (probe, logger) = Create(new SandboxOptions(), client);

        await probe.StartAsync(CancellationToken.None);

        logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Error);
        logger.Entries.ShouldContain(entry => entry.Message.Contains("runsc is registered"));
    }

    [Test]
    public async Task StartAsync_WhenRunscIsMissing_LogsTheFixWithoutThrowing()
    {
        var client = ClientWithRuntimes("runc");
        var (probe, logger) = Create(new SandboxOptions(), client);

        await Should.NotThrowAsync(() => probe.StartAsync(CancellationToken.None));

        var error = logger.Entries.Where(entry => entry.Level == LogLevel.Error).ShouldHaveSingleItem();
        error.Message.ShouldContain("runsc");
        error.Message.ShouldContain("runsc install -- --host-uds=create");
        error.Message.ShouldContain("Ai:Sandbox:UseGVisorRuntime=false");
    }

    [Test]
    public async Task StartAsync_WhenGVisorIsDisabled_DoesNotQueryDocker()
    {
        var client = Substitute.For<IDockerClient>();
        var (probe, logger) = Create(new SandboxOptions { UseGVisorRuntime = false }, client);

        await probe.StartAsync(CancellationToken.None);

        logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Error);
        await client.System.DidNotReceive().GetSystemInfoAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StartAsync_WhenSandboxIsDisabled_DoesNotQueryDocker()
    {
        var client = Substitute.For<IDockerClient>();
        var (probe, logger) = Create(new SandboxOptions { Enabled = false }, client);

        await probe.StartAsync(CancellationToken.None);

        logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Error);
        await client.System.DidNotReceive().GetSystemInfoAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StartAsync_WhenTheDaemonIsUnreachable_DoesNotEscapeStartup()
    {
        var client = Substitute.For<IDockerClient>();
        client.System
            .GetSystemInfoAsync(Arg.Any<CancellationToken>())
            .Returns<Task<SystemInfoResponse>>(_ => throw new HttpRequestException("daemon unavailable"));

        var (probe, logger) = Create(new SandboxOptions(), client);

        await Should.NotThrowAsync(() => probe.StartAsync(CancellationToken.None));

        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("runtime probe failed"));
    }
}
