namespace Dotto.Ai.Sandbox;

/// <summary>
/// Thrown when sandbox functionality is used while the sandbox is disabled
/// through configuration (<c>Ai:Sandbox:Enabled=false</c>).
/// </summary>
public sealed class SandboxDisabledException() : InvalidOperationException(DefaultMessage)
{
    public const string DefaultMessage =
        "The AI sandbox is disabled (Ai:Sandbox:Enabled=false). " +
        "Sandbox-backed tools are not available in this configuration; " +
        "set Ai:Sandbox:Enabled=true and configure the sandbox image to use them.";
}
