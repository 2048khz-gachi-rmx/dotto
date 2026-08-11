namespace Dotto.Ai.Sandbox;

/// <summary>
/// The per-invocation sandbox entity. One scoped instance exists per agent invocation;
/// it owns the Docker client and encapsulates the whole lifecycle of the session's sandbox container.
/// Disposal stops/removes the container (if one was started) and deletes the host session directory.
/// </summary>
/// <remarks>
/// Exposes two segments:
/// <list type="bullet">
/// <item><see cref="Metadata"/> — always present after <see cref="InitializeAsync"/> (eagerly created host dir).
/// Filesystem-only tools read from here and don't need to start the container.</item>
/// <item><see cref="Container"/> — nullable; before using, must be populated by <see cref="EnsureStartedAsync"/>.</item>
/// </list>
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
    SandboxContainer? Container { get; }

    /// <summary>
    /// Eagerly creates the host session directory and materializes <see cref="Metadata"/>.
    /// Idempotent; safe to call once per session.
    /// </summary>
    Task<SandboxMetadata> InitializeAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Ensures the sandbox container is running and ready. Safe to call multiple
    /// times — only starts the container once. Returns the container segment.
    /// </summary>
    Task<SandboxContainer> EnsureStartedAsync(CancellationToken ct);
}
