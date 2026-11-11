namespace Dotto.Ai.Sandbox;

/// <summary>
/// Session ID, owning caller, and the host directory bind-mounted as /storage.
/// </summary>
public sealed record SandboxMetadata(string SessionId, ulong CallerId, string HostStorageDir);
