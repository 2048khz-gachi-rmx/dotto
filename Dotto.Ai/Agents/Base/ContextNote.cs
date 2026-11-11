namespace Dotto.Ai.Agents.Base;

/// <summary>
/// A runtime fact for the system prompt: environment quirks or caller state that is
/// not part of the conversation history. Rendered by the agent's prompt template.
/// </summary>
public sealed record ContextNote(string Title, string Body);
