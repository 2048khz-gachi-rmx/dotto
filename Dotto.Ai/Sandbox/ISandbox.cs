namespace Dotto.Ai.Sandbox;

/// <summary>
/// Per-invocation sandbox: owns the container lifecycle and its storage.
/// </summary>
public interface ISandbox : IAsyncDisposable
{
    /// <summary>
    /// Metadata; set by <see cref="InitializeAsync"/>.
    /// </summary>
    SandboxMetadata Metadata { get; }

    /// <summary>
    /// Started container segment, or <c>null</c> before it is created.
    /// </summary>
    ISandboxContainer? Container { get; }

    /// <summary>
    /// Creates the per-caller storage directory and materializes <see cref="Metadata"/>.
    /// </summary>
    Task<SandboxMetadata> InitializeAsync(string sessionId, ulong callerId, CancellationToken ct);

    /// <summary>
    /// Starts the container once and returns it.
    /// </summary>
    Task<ISandboxContainer> EnsureStartedAsync(CancellationToken ct);
}
