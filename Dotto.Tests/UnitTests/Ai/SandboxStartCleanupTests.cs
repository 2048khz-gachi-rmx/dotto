using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// A failed start must not orphan the created container.
/// </summary>
public class SandboxStartCleanupTests
{
    private const string CreatedContainerId = "created-container-id";

    private static SandboxOptions DefaultOptions(string storageBase) => new()
    {
        Enabled = true,
        StorageBasePath = storageBase,
        // Never use the production IPC path from a unit test.
        IpcBasePath = IpcBase(storageBase),
        // Short enough to keep the timeout test fast; SandboxOptions validation
        // is not involved when options are constructed directly in a unit test.
        ContainerStartTimeout = TimeSpan.FromMilliseconds(300),
        // Production mode: no InspectContainerAsync host-port lookup needed.
        NetworkName = "test-network"
    };

    private static string IpcBase(string storageBase) => storageBase + "-ipc";

    private static string IpcSessionDir(string storageBase) => Path.Combine(IpcBase(storageBase), "session");

    private static void Cleanup(string storageBase)
    {
        if (Directory.Exists(storageBase))
            Directory.Delete(storageBase, true);
        if (Directory.Exists(IpcBase(storageBase)))
            Directory.Delete(IpcBase(storageBase), true);
    }

    private static Sandbox CreateSandbox(
        IDockerClient client,
        string storageBase,
        ISandboxContainer? probeContainer = null)
    {
        var options = Options.Create(DefaultOptions(storageBase));

        // Readiness probe seam: a fake container never touches the network.
        return probeContainer is null
            ? new Sandbox(options, NullLogger<Sandbox>.Instance, client)
            : new Sandbox(options, NullLogger<Sandbox>.Instance, client)
            {
                ContainerFactory = (_, _, _) => probeContainer
            };
    }

    private static IDockerClient ClientThatCreatesContainer()
    {
        var client = Substitute.For<IDockerClient>();
        client.Containers
            .CreateContainerAsync(Arg.Any<CreateContainerParameters>(), Arg.Any<CancellationToken>())
            .Returns(new CreateContainerResponse { ID = CreatedContainerId });
        return client;
    }

    private static string NewStorageBase()
        => Path.Combine(Path.GetTempPath(), "dotto-sandbox-start-" + Guid.NewGuid().ToString("N"));

    [Test]
    public async Task EnsureStartedAsync_WhenStartFails_StopsCreatedContainer()
    {
        // Arrange
        var client = ClientThatCreatesContainer();
        client.Containers
            .StartContainerAsync(
                Arg.Any<string>(), Arg.Any<ContainerStartParameters>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("daemon refused to start the container"));

        var storageBase = NewStorageBase();
        var sandbox = CreateSandbox(client, storageBase);

        try
        {
            await sandbox.InitializeAsync("session", 42, CancellationToken.None);

            // Act
            await Should.ThrowAsync<InvalidOperationException>(
                () => sandbox.EnsureStartedAsync(CancellationToken.None));

            // Assert — the created container is stopped, and no public handle leaks.
            await client.Containers.Received(1).StopContainerAsync(
                CreatedContainerId,
                Arg.Any<ContainerStopParameters>(),
                Arg.Any<CancellationToken>());
            sandbox.Container.ShouldBeNull();
        }
        finally
        {
            await sandbox.DisposeAsync();

            Cleanup(storageBase);
        }
    }

    [Test]
    public async Task EnsureStartedAsync_WhenHealthPollTimesOut_StopsCreatedContainer()
    {
        // Arrange — start succeeds, but the container never answers /ping.
        var client = ClientThatCreatesContainer();
        client.Containers
            .StartContainerAsync(
                Arg.Any<string>(), Arg.Any<ContainerStartParameters>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var probe = Substitute.For<ISandboxContainer>();
        probe.Endpoint.Returns(SandboxEndpoint.FromUri(new Uri("http://sandbox-never-ready:8080")));
        probe.PingAsync(Arg.Any<CancellationToken>()).Returns(false);

        var storageBase = NewStorageBase();
        var sandbox = CreateSandbox(client, storageBase, probe);

        try
        {
            await sandbox.InitializeAsync("session", 43, CancellationToken.None);

            // Act
            var ex = await Should.ThrowAsync<Exception>(
                () => sandbox.EnsureStartedAsync(CancellationToken.None));

            // Assert — whichever way the readiness wait aborts, teardown ran.
            ex.Message.ShouldNotBeNullOrWhiteSpace();
            await client.Containers.Received(1).StopContainerAsync(
                CreatedContainerId,
                Arg.Any<ContainerStopParameters>(),
                Arg.Any<CancellationToken>());
            sandbox.Container.ShouldBeNull();
        }
        finally
        {
            await sandbox.DisposeAsync();

            Cleanup(storageBase);
        }
    }

    [Test]
    public async Task EnsureStartedAsync_WhenStartFails_RemovesSessionIpcDirectory()
    {
        // Arrange — the created container never starts, so its socket dir has no user.
        var client = ClientThatCreatesContainer();
        client.Containers
            .StartContainerAsync(
                Arg.Any<string>(), Arg.Any<ContainerStartParameters>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("runsc is not a registered runtime"));

        var storageBase = NewStorageBase();
        var sandbox = CreateSandbox(client, storageBase);

        try
        {
            await sandbox.InitializeAsync("session", 45, CancellationToken.None);

            // Act
            await Should.ThrowAsync<InvalidOperationException>(
                () => sandbox.EnsureStartedAsync(CancellationToken.None));

            // Assert — the failure path cleans the IPC directory before rethrowing.
            Directory.Exists(IpcSessionDir(storageBase)).ShouldBeFalse();
        }
        finally
        {
            await sandbox.DisposeAsync();
            Cleanup(storageBase);
        }
    }

    [Test]
    public async Task DisposeAsync_RemovesSessionIpcDirectory()
    {
        // Arrange — a ready container, so the IPC directory is in active use.
        var client = ClientThatCreatesContainer();
        client.Containers
            .StartContainerAsync(
                Arg.Any<string>(), Arg.Any<ContainerStartParameters>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var probe = Substitute.For<ISandboxContainer>();
        probe.PingAsync(Arg.Any<CancellationToken>()).Returns(true);

        var storageBase = NewStorageBase();
        var sandbox = CreateSandbox(client, storageBase, probe);

        try
        {
            await sandbox.InitializeAsync("session", 46, CancellationToken.None);
            await sandbox.EnsureStartedAsync(CancellationToken.None);

            Directory.Exists(IpcSessionDir(storageBase)).ShouldBeTrue();
        }
        finally
        {
            // Act
            await sandbox.DisposeAsync();

            // Assert
            Directory.Exists(IpcSessionDir(storageBase)).ShouldBeFalse();
            Cleanup(storageBase);
        }
    }

    [Test]
    public async Task DisposeAsync_WhenStartFailed_DoesNotThrow()
    {
        // EnsureStartedAsync already cleaned up; dispose must tolerate a missing container.
        var client = ClientThatCreatesContainer();
        client.Containers
            .StartContainerAsync(
                Arg.Any<string>(), Arg.Any<ContainerStartParameters>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("daemon refused to start the container"));
        client.Containers
            .StopContainerAsync(
                Arg.Any<string>(), Arg.Any<ContainerStopParameters>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("docker daemon unavailable"));

        var storageBase = NewStorageBase();
        var sandbox = CreateSandbox(client, storageBase);

        try
        {
            await sandbox.InitializeAsync("session", 44, CancellationToken.None);
            await Should.ThrowAsync<InvalidOperationException>(
                () => sandbox.EnsureStartedAsync(CancellationToken.None));
        }
        finally
        {
            // Act + Assert
            await Should.NotThrowAsync(() => sandbox.DisposeAsync().AsTask());

            Cleanup(storageBase);
        }
    }
}
