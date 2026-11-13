using System.Net.Sockets;
using System.Text;
using Dotto.Ai.Sandbox;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>Exercises the socket transport against a real AF_UNIX listener.</summary>
public class SandboxContainerTransportTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dotto-sandbox-uds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Answers one HTTP/1.1 request and returns it.</summary>
    private static async Task<string> ServeOnceAsync(
        Socket listener,
        string body,
        CancellationToken ct)
    {
        using var connection = await listener.AcceptAsync(ct);

        var request = new StringBuilder();
        var buffer = new byte[4096];

        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await connection.ReceiveAsync(buffer, SocketFlags.None, ct);
            if (read == 0)
                break;
            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        var payload = Encoding.UTF8.GetBytes(body);
        var headers = $"HTTP/1.1 200 OK\r\n" +
                      $"Content-Type: application/json\r\n" +
                      $"Content-Length: {payload.Length}\r\n" +
                      $"Connection: close\r\n\r\n";

        await connection.SendAsync(Encoding.ASCII.GetBytes(headers), SocketFlags.None, ct);
        await connection.SendAsync(payload, SocketFlags.None, ct);

        return request.ToString();
    }

    [Test]
    public async Task PingAsync_OverUnixSocket_SucceedsAndSendsToken()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("AF_UNIX is not available on Windows");

        var dir = NewTempDir();
        var socketPath = Path.Combine(dir, "sandbox.sock");

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var serving = ServeOnceAsync(listener, "{\"status\":\"ok\"}", cts.Token);

            var container = new SandboxContainer(
                SandboxEndpoint.FromSocketPath(socketPath), "container-id", "test-token");

            container.Endpoint.IsUnixSocket.ShouldBeTrue();

            var ready = await container.PingAsync(cts.Token);
            ready.ShouldBeTrue();

            var request = await serving;

            // Synthetic base URI; same token header as TCP.
            request.ShouldContain("GET /ping HTTP/1.1");
            request.ShouldContain("Host: sandbox");
            request.ShouldContain("X-Sandbox-Token: test-token");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Test]
    public void FromUri_KeepsTcpEndpoint()
    {
        var uri = new Uri("http://127.0.0.1:8080");
        var endpoint = SandboxEndpoint.FromUri(uri);

        endpoint.IsUnixSocket.ShouldBeFalse();
        endpoint.SocketPath.ShouldBeNull();
        endpoint.ApiUrl.ShouldBe(uri);
        endpoint.ToString().ShouldBe("http://127.0.0.1:8080/");
    }
}
