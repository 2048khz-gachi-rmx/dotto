namespace Dotto.Ai.Sandbox;

/// <summary>
/// Execution, health check, and file streaming over the sandbox HTTP API.
/// </summary>
public interface ISandboxContainer
{
    string ContainerId { get; }

    /// <summary>Network URI or per-session Unix socket path.</summary>
    SandboxEndpoint Endpoint { get; }

    /// <summary>
    /// Polls <c>GET /ping</c>. Returns <c>true</c> when it responds with HTTP 200.
    /// </summary>
    Task<bool> PingAsync(CancellationToken ct);

    /// <summary>
    /// Executes a shell command in the sandbox via <c>POST /execute</c>.
    /// </summary>
    Task<SandboxExecuteResult> ExecuteAsync(
        string command,
        string cwd = "",
        string timeout = "",
        CancellationToken ct = default);

    /// <summary>
    /// Streams a file out of the sandbox via <c>GET /file</c>. The returned
    /// stream stays open until disposal and must be consumed before the
    /// container is stopped.
    /// </summary>
    Task<SandboxFile> GetFileAsync(string sandboxPath, CancellationToken ct);

    /// <summary>
    /// Streams a file into the sandbox via <c>PUT /file</c>. When
    /// <paramref name="dedupe"/> is set the server appends a numeric suffix on
    /// collision; the effective sandbox path is returned.
    /// </summary>
    Task<string> PutFileAsync(
        string sandboxPath,
        Stream content,
        long length,
        bool dedupe,
        CancellationToken ct);
}
