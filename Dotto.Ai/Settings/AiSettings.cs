using System.ComponentModel.DataAnnotations;
using Dotto.Ai.Prompts;

namespace Dotto.Ai.Settings;

public class AiSettings
{
    public required Uri BaseUrl { get; set; }

    public required string ApiKey { get; set; }

    public required string AssistantModel { get; set; }

    public Dictionary<string, PromptSource> Prompts { get; set; } = [];

    /// <summary>
    /// Per-assistant overrides. Falls back to the top-level AiSettings values
    /// when a property is null.
    /// </summary>
    [Required]
    public ChatAssistantSettings ChatAssistant { get; set; } = null!;
}

public class ChatAssistantSettings
{
    /// <summary>
    /// Overrides <c>AiSettings.AssistantModel</c> for this assistant.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Overrides <c>AiSettings.BaseUrl</c> for this assistant.
    /// </summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// Overrides <c>AiSettings.ApiKey</c> for this assistant.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Number of recent channel messages to include as conversation history.
    /// </summary>
    public int HistoryContextSize { get; set; } = 30;
    
    /// <summary>
    /// Amount of time before the message is excluded from the context.
    /// </summary>
    public TimeSpan HistoryMaxAge { get; set; } = TimeSpan.FromDays(2);
}