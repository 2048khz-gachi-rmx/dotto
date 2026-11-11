using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Fast (Docker-free) assertions on the container parameters the sandbox
/// sends. The integration suite needs a real daemon and is [Explicit], so the
/// mount options have no other guard.
/// </summary>
public class SandboxContainerParametersTests
{
    private static string[] TmpfsOptions(CreateContainerParameters parameters, string mount)
        => parameters.HostConfig!.Tmpfs![mount].Split(',', StringSplitOptions.RemoveEmptyEntries);

    [Test]
    public async Task EnsureStartedAsync_MountsWorkAsWritableTmpfs()
    {
        var storageBase = Path.Combine(
            Path.GetTempPath(), "dotto-sandbox-params-" + Guid.NewGuid().ToString("N"));

        var client = Substitute.For<IDockerClient>();
        CreateContainerParameters? captured = null;
        client.Containers
            .CreateContainerAsync(Arg.Do<CreateContainerParameters>(p => captured = p), Arg.Any<CancellationToken>())
            .Returns(new CreateContainerResponse { ID = "container-id" });

        var container = Substitute.For<ISandboxContainer>();
        container.ApiUrl.Returns(new Uri("http://127.0.0.1:8080"));
        container.ContainerId.Returns("container-id");
        container.PingAsync(Arg.Any<CancellationToken>()).Returns(true);

        var sandbox = new Sandbox(
            Options.Create(new SandboxOptions
            {
                ImageTag = "dotto-sandbox:latest",
                NetworkName = "sandbox-test",
                StorageBasePath = storageBase,
                WorkTmpfsMb = 256,
                MemoryMb = 512,
            }),
            NullLogger<Sandbox>.Instance,
            client)
        {
            ContainerFactory = (_, _, _) => container
        };

        try
        {
            await sandbox.InitializeAsync("session", 1, CancellationToken.None);
            await sandbox.EnsureStartedAsync(CancellationToken.None);
        }
        finally
        {
            await sandbox.DisposeAsync();

            if (Directory.Exists(storageBase))
                Directory.Delete(storageBase, true);
        }

        captured.ShouldNotBeNull();

        // Docker copies the image directory's owner/mode onto the tmpfs root and
        // mounts tmpfs noexec by default. `WORKDIR /work` is root:root 2755, so
        // without explicit options the non-root sandbox user cannot write there
        // (this broke every DownloadMedia call and any command writing to /work).
        var work = TmpfsOptions(captured!, "/work");
        work.ShouldContain("size=256M");
        work.ShouldContain("mode=1777");
        work.ShouldContain("exec");
        work.ShouldNotContain("noexec");
    }
}
