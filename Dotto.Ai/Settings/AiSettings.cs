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

    /// <summary>
    /// Maximum number of attachments the assistant may return per response.
    /// </summary>
    [Range(1, 100)]
    public int MaxAttachments { get; set; } = 10;

    /// <summary>
    /// Maximum number of turns (model responses that may call tools) a single run may use.
    /// A turn may request any number of tool calls; calls in later turns are refused and the
    /// model is told to answer with what it has.
    /// </summary>
    [Range(1, 100)]
    public int MaxTurns { get; set; } = 20;

    /// <summary>
    /// Runs that used at least this many turns include the tool transcript in the
    /// final answer. Leave unset to use <see cref="MaxTurns"/>: the transcript is for runs
    /// that spent the whole budget, which is also when the footer invites the question.
    /// </summary>
    [Range(1, 100)]
    public int? TranscriptTurns { get; set; }
}