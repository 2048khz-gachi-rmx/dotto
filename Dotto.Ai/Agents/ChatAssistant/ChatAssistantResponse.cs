using Dotto.Ai.Agents.Base;

namespace Dotto.Ai.Agents.ChatAssistant;

public class ChatAssistantResponse
{
    /// <summary>
    /// The agent's text response.
    /// </summary>
    public string? Response { get; set; }
    
    /// <summary>
    /// Populated when the agent invokes an upload tool. These are passed into the response.
    /// </summary>
    public List<AgentAttachment> Attachments { get; set; } = [];
}