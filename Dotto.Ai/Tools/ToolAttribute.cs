
namespace Dotto.Ai.Tools;

/// <summary>
/// Marker attribute applied to a method
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal class ToolAttribute(
    ToolAttribute.ToolTypeFlag type,
    string? name = null,
    bool sandboxBacked = true,
    bool exemptFromTurnLimit = false) : Attribute
{
    [Flags]
    public enum ToolTypeFlag
    {
        /// <summary>
        /// This tool will be available to the UserAssistant agent
        /// </summary>
        UserAssistant = 1,
    }

    public ToolTypeFlag ToolType { get; } = type;
    public string? Name { get; } = name;

    /// <summary>
    /// Omitted from the tool list when the sandbox is disabled.
    /// </summary>
    public bool SandboxBacked { get; } = sandboxBacked;

    /// <summary>
    /// Still executes after the turn budget is spent. For delivery tools: a file the model
    /// already prepared must reach the user even if the run ran out of turns.
    /// </summary>
    public bool ExemptFromTurnLimit { get; } = exemptFromTurnLimit;
}