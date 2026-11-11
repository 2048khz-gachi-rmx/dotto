namespace Dotto.Ai.Agents.Base;

public class AgentContext
{
    public required ulong CallerId { get; init; }
    public required string CallerName { get; init; }
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Optional per-run tool-call record. The tool wrapper observes it and the
    /// caller renders it; tools themselves stay unaware of it.
    /// </summary>
    public AgentProgress? Progress { get; init; }

    /// <summary>
    /// Extra facts for this invocation; the caller layer fills these when the
    /// environment differs from the default.
    /// </summary>
    public List<ContextNote> Notes { get; init; } = [];
}