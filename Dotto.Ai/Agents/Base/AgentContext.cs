namespace Dotto.Ai.Agents.Base;

public class AgentContext
{
    public required ulong CallerId { get; init; }
    public required string CallerName { get; init; }
    public string SessionId { get; set; } = string.Empty;
}