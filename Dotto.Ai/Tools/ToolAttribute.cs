
namespace Dotto.Ai.Tools;

/// <summary>
/// Marker attribute applied to a method
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal class ToolAttribute(ToolAttribute.ToolTypeFlag type, string? name = null) : Attribute
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
}