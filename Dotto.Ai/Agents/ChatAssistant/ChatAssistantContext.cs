using Dotto.Ai.Agents.Base;
using Microsoft.Extensions.AI;

namespace Dotto.Ai.Agents.ChatAssistant;

public class ChatAssistantContext : AgentContext
{
    public required ulong ChannelId { get; init; }
    public required ulong GuildId { get; init; }
    public List<ChatMessage> Messages { get; set; } = [];

    /// <summary>
    /// Populated when the agent invokes an upload tool. These are passed into the response.
    /// </summary>
    public List<AgentAttachment> Attachments { get; set; } = [];
}