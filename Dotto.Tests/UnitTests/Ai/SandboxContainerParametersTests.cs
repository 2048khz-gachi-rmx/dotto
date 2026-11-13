using Docker.DotNet;
using Docker.DotNet.Models;
using Dotto.Ai.Sandbox;
using Dotto.Ai.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>Docker-free assertions on container parameters and transport selection.</summary>
public class SandboxContainerParametersTests
{
    private sealed class CapturedRun
    {
        public required CreateContainerParameters Parameters { get; init; }
        public required SandboxEndpoint Endpoint { get; init; }
        public required string StorageBase { get; init; }
        public required string IpcBase { get; init; }
        public string? ResolvConf { get; init; }

        public void Cleanup()
        {
            DeleteIfExists(StorageBase);
            DeleteIfExists(IpcBase);
        }

        private static void DeleteIfExists(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    private static string[] TmpfsOptions(CreateContainerParameters parameters, string mount)
        => parameters.HostConfig!.Tmpfs![mount].Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static SandboxOptions CreateOptions(Action<SandboxOptions>? configure = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var options = new SandboxOptions
        {
            ImageTag = "dotto-sandbox:latest",
            NetworkName = "sandbox-test",
            StorageBasePath = Path.Combine(Path.GetTempPath(), "dotto-sandbox-params-" + id),
            IpcBasePath = Path.Combine(Path.GetTempPath(), "dotto-sandbox-ipc-" + id),
            DnsServers = ["1.1.1.1", "8.8.8.8"],
            WorkTmpfsMb = 256,
            MemoryMb = 512,
        };

        configure?.Invoke(options);
        return options;
    }

    private static async Task<CapturedRun> StartAsync(SandboxOptions options)
    {
        var client = Substitute.For<IDockerClient>();
        CreateContainerParameters? captured = null;
        client.Containers
            .CreateContainerAsync(Arg.Do<CreateContainerParameters>(p => captured = p), Arg.Any<CancellationToken>())
            .Returns(new CreateContainerResponse { ID = "container-id" });

        var container = Substitute.For<ISandboxContainer>();
        container.ContainerId.Returns("container-id");
        container.PingAsync(Arg.Any<CancellationToken>()).Returns(true);

        SandboxEndpoint? endpoint = null;
        string? resolvConf = null;
        var sandbox = new Sandbox(Options.Create(options), NullLogger<Sandbox>.Instance, client)
        {
            ContainerFactory = (e, _, _) =>
            {
                endpoint = e;
                var path = Path.Combine(options.IpcBasePath, "resolv.conf");
                if (File.Exists(path))
                    resolvConf = File.ReadAllText(path);
                return container;
            }
        };

        try
        {
            await sandbox.InitializeAsync("session", 1, CancellationToken.None);
            await sandbox.EnsureStartedAsync(CancellationToken.None);
        }
        finally
        {
            await sandbox.DisposeAsync();
        }

        captured.ShouldNotBeNull();
        endpoint.ShouldNotBeNull();

        return new CapturedRun
        {
            Parameters = captured!,
            Endpoint = endpoint!,
            StorageBase = options.StorageBasePath,
            IpcBase = options.IpcBasePath,
            ResolvConf = resolvConf,
        };
    }

    [Test]
    public async Task EnsureStartedAsync_MountsWorkAsWritableTmpfs()
    {
        var run = await StartAsync(CreateOptions());

        try
        {
            // Docker copies the image directory's owner/mode onto the tmpfs root and
            // mounts tmpfs noexec by default. `WORKDIR /work` is root:root 2755, so
            // without explicit options the non-root sandbox user cannot write there
            // (this broke every DownloadMedia call and any command writing to /work).
            var work = TmpfsOptions(run.Parameters, "/work");
            work.ShouldContain("size=256M");
            work.ShouldContain("mode=1777");
            work.ShouldContain("exec");
            work.ShouldNotContain("noexec");
        }
        finally
        {
            run.Cleanup();
        }
    }

    [Test]
    public async Task EnsureStartedAsync_DefaultsToGVisorRuntime()
    {
        var options = CreateOptions();

        options.UseGVisorRuntime.ShouldBeTrue();

        var run = await StartAsync(options);

        try
        {
            run.Parameters.HostConfig!.Runtime.ShouldBe("runsc");
        }
        finally
        {
            run.Cleanup();
        }
    }

    [Test]
    public async Task EnsureStartedAsync_WithGVisorDisabled_UsesDaemonDefaultRuntime()
    {
        var run = await StartAsync(CreateOptions(options => options.UseGVisorRuntime = false));

        try
        {
            // null = no explicit runtime, i.e. the daemon default (runc).
            run.Parameters.HostConfig!.Runtime.ShouldBeNull();
        }
        finally
        {
            run.Cleanup();
        }
    }

    [Test]
    public async Task EnsureStartedAsync_WithNetworkName_UsesUnixSocketTransport()
    {
        var run = await StartAsync(CreateOptions());

        try
        {
            var sessionDir = Path.Combine(run.IpcBase, "session");
            var socketPath = Path.Combine(sessionDir, "sandbox.sock");

            run.Parameters.HostConfig!.Binds.ShouldContain($"{sessionDir}:/run/dotto");
            run.Parameters.Env.ShouldContain("SANDBOX_SOCKET=/run/dotto/sandbox.sock");

            // Docker writes 127.0.0.11 for user-defined networks; gVisor needs a static file.
            var resolvConf = Path.Combine(run.IpcBase, "resolv.conf");
            var gaiConf = Path.Combine(run.IpcBase, "gai.conf");
            run.Parameters.HostConfig.Binds.ShouldContain($"{resolvConf}:/etc/resolv.conf:ro");
            run.Parameters.HostConfig.Binds.ShouldContain($"{gaiConf}:/etc/gai.conf:ro");
            run.ResolvConf.ShouldNotBeNull();
            run.ResolvConf.ShouldContain("nameserver 1.1.1.1");
            run.ResolvConf.ShouldContain("options timeout:2 attempts:2");
            run.ResolvConf.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.StartsWith("nameserver ", StringComparison.Ordinal))
                .ShouldBe(2);
            run.ResolvConf.ShouldContain("nameserver 8.8.8.8");

            run.Parameters.HostConfig.PortBindings.ShouldBeNull();
            run.Parameters.ExposedPorts.ShouldBeNull();

            run.Endpoint.IsUnixSocket.ShouldBeTrue();
            run.Endpoint.SocketPath.ShouldBe(socketPath);
        }
        finally
        {
            run.Cleanup();
        }
    }
}
