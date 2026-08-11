namespace Dotto.Ai.Abstractions;

/// <summary>
/// Always-present session metadata for the sandbox: the session ID and the
/// eagerly-created host directory that is bind-mounted as <c>/work</c> when the
/// container is eventually started. Operations that only need the filesystem
/// (e.g. <c>DownloadMedia</c>, <c>UploadFile</c>) read from here and never
/// touch the container.
/// </summary>
public sealed record SandboxMetadata(string SessionId, string HostSessionDir);
