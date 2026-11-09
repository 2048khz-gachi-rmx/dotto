namespace Dotto.Ai.Sandbox;

/// <summary>
/// The per-invocation sandbox entity. One scoped instance exists per agent invocation;
/// it owns the Docker client and encapsulates the whole lifecycle of the session's sandbox container.
/// Disposal stops/removes the container (if one was started) but never deletes
/// persistent storage.
/// </summary>
/// <remarks>
/// The bot never performs filesystem operations on sandbox-authored paths.
/// All file traffic crosses the boundary as byte streams through the sandbox
/// HTTP API (<see cref="ISandboxContainer"/>), so a path planted by the sandbox
/// is never resolved by the bot in its own namespace.
/// </remarks>
public interface ISandbox : IAsyncDisposable
{
    /// <summary>
    /// Always-present metadata for this session. Set by <see cref="InitializeAsync"/>.
    /// </summary>
    SandboxMetadata Metadata { get; }

    /// <summary>
    /// The started container segment, or <c>null</c> if the container has not been
    /// created yet. Populated lazily by <see cref="EnsureStartedAsync"/>.
    /// </summary>
    ISandboxContainer? Container { get; }

    /// <summary>
    /// Creates the per-caller storage directory and materializes
    /// <see cref="Metadata"/>. Idempotent; safe to call once per session.
    /// </summary>
    Task<SandboxMetadata> InitializeAsync(string sessionId, ulong callerId, CancellationToken ct);

    /// <summary>
    /// Ensures the sandbox container is running and ready. Safe to call multiple
    /// times — only starts the container once. Returns the container segment.
    /// </summary>
    Task<ISandboxContainer> EnsureStartedAsync(CancellationToken ct);
}
