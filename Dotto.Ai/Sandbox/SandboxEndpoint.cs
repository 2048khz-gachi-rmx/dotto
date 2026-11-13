namespace Dotto.Ai.Sandbox;

/// <summary>A network URI (development) or a host Unix socket path (production).</summary>
public sealed record SandboxEndpoint
{
    private static readonly Uri SocketBaseUri = new("http://sandbox/");

    private SandboxEndpoint(Uri apiUrl, string? socketPath)
    {
        ApiUrl = apiUrl;
        SocketPath = socketPath;
    }

    /// <summary>Synthetic base URI for socket requests; not a reachable host.</summary>
    public Uri ApiUrl { get; }

    public string? SocketPath { get; }

    public bool IsUnixSocket => SocketPath != null;

    public static SandboxEndpoint FromUri(Uri apiUrl) => new(apiUrl, null);

    public static SandboxEndpoint FromSocketPath(string socketPath)
        => new(SocketBaseUri, socketPath);

    public override string ToString() => SocketPath ?? ApiUrl.ToString();
}
