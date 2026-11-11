namespace Dotto.Ai.Sandbox;

/// <summary>
/// Strings returned to the model when the sandbox itself is unavailable.
/// </summary>
internal static class SandboxToolMessages
{
    /// <summary>
    /// Tool result when the container cannot start (see the sandbox-lifecycle spec).
    /// </summary>
    public const string StartFailed =
        "Execution sandbox failed to start; consider this tool disabled.";
}
