namespace Dotto.Ai.Sandbox;

/// <summary>
/// Materialized session metadata: the session ID, the caller the persistent
/// storage belongs to, and the host directory bind-mounted as <c>/storage</c>.
/// The bot only ever creates this directory — it never reads or writes the
/// files the sandbox puts in it.
/// </summary>
public sealed record SandboxMetadata(string SessionId, ulong CallerId, string HostStorageDir);
