using System.Globalization;
using System.Text;
using Dotto.Ai.Tools;

namespace Dotto.Ai.Agents.Base;

/// <summary>
/// Per-run record of tool calls: what the agent did, whether it failed, how much of the
/// turn budget was used, and what (if anything) the user should be told about it.
/// Created by the caller before the run and shared with the tool wrapper through
/// <see cref="AgentContext.Progress"/>.
/// </summary>
public sealed class AgentProgress(int maxTurns)
{
    /// <summary>Characters kept from a tool argument in the activity line and transcript.</summary>
    public const int ArgumentPreviewLength = 120;

    /// <summary>Characters kept from a tool result in the activity line and transcript.</summary>
    public const int ResultPreviewLength = 180;

    /// <summary>Appended when a preview had to be cut.</summary>
    public const char TruncationMarker = '…';

    /// <summary>How many recent calls the live activity line shows.</summary>
    public const int ActivityLines = 5;

    /// <summary>Default character budget for the transcript block.</summary>
    public const int TranscriptBudget = 1200;

    public const string ApproachingLimitWarning =
        "Turn limit warning: you are approaching the tool-call limit for this run. " +
        "Start wrapping up and prepare to answer with the information you have.";

    public const string LastTurnWarning =
        "Turn limit warning: this is your last turn. " +
        "Respond now with the information you have; further tool calls will be refused.";

    public const string TurnLimitNotice =
        "Turn limit reached: no further tool calls will be executed. " +
        "Answer now with the information you have.";

    private readonly object _gate = new();
    private readonly List<ToolCall> _calls = [];
    private readonly List<string> _warnings = [];

    /// <summary>Maximum number of tool calls this run may execute.</summary>
    public int MaxTurns { get; } = Math.Max(1, maxTurns);

    /// <summary>Raised whenever a call starts, completes or is refused.</summary>
    public event Action? Changed;

    /// <summary>Executed tool calls (refused attempts are not turns).</summary>
    public int Turns
    {
        get
        {
            lock (_gate)
                return _calls.Count(call => call.Executed);
        }
    }

    /// <summary>Tool calls the run was asked to make after the budget was exhausted.</summary>
    public int RefusedCalls
    {
        get
        {
            lock (_gate)
                return _calls.Count(call => !call.Executed);
        }
    }

    public IReadOnlyList<ToolCall> Calls
    {
        get
        {
            lock (_gate)
                return _calls.ToArray();
        }
    }

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_gate)
                return _warnings.ToArray();
        }
    }

    /// <summary>
    /// Records a call as started. Returns <c>null</c> when the turn budget is exhausted,
    /// in which case the caller must refuse the call instead of executing it.
    /// </summary>
    public ToolCall? StartCall(string toolName, string argumentPreview)
    {
        ToolCall call;
        lock (_gate)
        {
            var executed = _calls.Count(c => c.Executed);
            call = executed >= MaxTurns
                ? ToolCall.Refused(toolName, argumentPreview, TurnLimitNotice)
                : ToolCall.Pending(toolName, argumentPreview, executed + 1);

            _calls.Add(call);
        }

        Changed?.Invoke();
        return call.Executed ? call : null;
    }

    /// <summary>
    /// Records the outcome of a call and returns the turn-limit warning that must be
    /// prepended to the tool result (empty when none applies).
    /// </summary>
    public string CompleteCall(ToolCall call, IToolResult? result)
    {
        string? warning;
        lock (_gate)
        {
            call.Result = result;
            call.Completed = true;

            warning = call.Executed ? SelectWarning(call.Index, MaxTurns) : null;
            if (warning != null)
                _warnings.Add(warning);
        }

        Changed?.Invoke();
        return warning ?? string.Empty;
    }

    /// <summary>
    /// Whether the final answer should carry the tool transcript. Unset means the turn
    /// limit, so only runs that spent their whole budget dump their history.
    /// </summary>
    public bool ShouldShowTranscript(int? transcriptTurns, bool verbose) =>
        verbose || Turns >= (transcriptTurns ?? MaxTurns);

    /// <summary>
    /// Renders the newest calls as the live activity line. Pending calls have no result part.
    /// </summary>
    public static string RenderActivity(IEnumerable<ToolCall> calls, int maxLines = ActivityLines) =>
        string.Join('\n', calls.TakeLast(Math.Max(1, maxLines)).Select(RenderLine));

    /// <summary>An activity line: subtext, so it reads as dimmed chatter above the answer.</summary>
    public static string RenderLine(ToolCall call) => "-# " + RenderCore(call);

    /// <summary>
    /// The call without its subtext marker, for lists that already supply their own bullet
    /// (a <c>-#</c> only renders as subtext at the start of a line).
    /// </summary>
    private static string RenderCore(ToolCall call)
    {
        var line = $"{call.ToolName}: {Code(call.ArgumentPreview)}";
        if (call is not { Completed: true, Result: { } result })
            return line;

        var rendered = RenderResult(result);
        return rendered.Length == 0 ? line : $"{line} **#>** {rendered}";
    }

    /// <summary>
    /// Formats a result for the activity line: the exit status when it was non-zero, then the
    /// stream worth showing or the tool's own text, both as inline code.
    /// </summary>
    private static string RenderResult(IToolResult result)
    {
        if (result is CommandToolResult command)
        {
            var status = command.ExitCode != 0 ? $"[{command.ExitCode}] " : string.Empty;
            var output = Preview(command.Output, ResultPreviewLength);

            return status + (output.Length > 0 ? Code(output) : string.Empty);
        }

        var text = Preview(result.Text, ResultPreviewLength);
        return text.Length > 0 ? Code(text) : string.Empty;
    }

    /// <summary>
    /// Wraps text in an inline code span so tool arguments and output render verbatim.
    /// Discord neither nests nor escapes backticks inside a span, so a literal backtick is
    /// swapped for a visually similar grave accent instead of breaking the formatting.
    /// </summary>
    public static string Code(string text) => $"`{text.Replace('`', 'ˋ')}`";

    /// <summary>
    /// Collapses whitespace and truncates to <paramref name="maxLength"/> characters
    /// (plus a truncation marker) so a preview always stays on one line.
    /// </summary>
    public static string Preview(string? text, int maxLength = ArgumentPreviewLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= maxLength)
            return collapsed;

        return string.Concat(
            collapsed.AsSpan(0, maxLength).TrimEnd(),
            TruncationMarker.ToString());
    }

    /// <summary>
    /// Renders the newest calls that fit into <paramref name="maxChars"/>, noting how many
    /// older calls were dropped.
    /// </summary>
    public string BuildTranscript(int maxChars = TranscriptBudget)
    {
        var calls = Calls;
        if (calls.Count == 0 || maxChars <= 0)
            return string.Empty;

        var header = $"Tool calls ({calls.Count}):";
        // Too small for even one line: report the count instead of blowing the budget.
        if (maxChars <= header.Length + 2)
            return $"{header}\n- (omitted)";

        var lines = new List<string>();
        var used = header.Length + 1;
        var omitted = 0;

        for (var i = calls.Count - 1; i >= 0; i--)
        {
            var line = "- " + RenderCore(calls[i]) + (calls[i].Failed ? " (failed)" : "");
            if (used + line.Length + 1 > maxChars)
            {
                omitted = i + 1;
                break;
            }

            lines.Add(line);
            used += line.Length + 1;
        }

        if (lines.Count == 0)
            return $"{header}\n- ({calls.Count} call(s) omitted)";

        lines.Reverse();

        var builder = new StringBuilder(header);
        if (omitted > 0)
            builder.Append('\n').Append("- ").Append(omitted).Append(" earlier call(s) omitted");

        foreach (var line in lines)
            builder.Append('\n').Append(line);

        return builder.ToString();
    }

    public static string FormatFooter(int turns, TimeSpan duration, int fileCount) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"-# {turns} turns · {Math.Round(duration.TotalSeconds)}s · {fileCount} file(s)");

    public string BuildFooter(TimeSpan duration, int fileCount) =>
        FormatFooter(Turns, duration, fileCount);

    private static string? SelectWarning(int callNumber, int maxTurns)
    {
        if (maxTurns >= 2 && callNumber == maxTurns - 1)
            return LastTurnWarning;

        if (maxTurns > 5 && callNumber == maxTurns - 5)
            return ApproachingLimitWarning;

        return null;
    }
}

/// <summary>
/// One tool call observed during a run.
/// </summary>
public sealed class ToolCall
{
    public required string ToolName { get; init; }

    public required string ArgumentPreview { get; init; }

    /// <summary>1-based executed call number; 0 for a refused call.</summary>
    public int Index { get; init; }

    /// <summary>False when the call was refused because the turn budget was exhausted.</summary>
    public bool Executed { get; init; } = true;

    public bool Completed { get; internal set; }

    /// <summary>The tool's outcome, exactly as the tool handed it back; null until completion.</summary>
    public IToolResult? Result { get; internal set; }

    /// <summary>Whether the result reports a failure. Text results never do on their own.</summary>
    public bool Failed => Result?.Failed ?? false;

    internal static ToolCall Pending(string toolName, string argumentPreview, int index) => new()
    {
        ToolName = toolName,
        ArgumentPreview = argumentPreview,
        Index = index
    };

    internal static ToolCall Refused(string toolName, string argumentPreview, string notice) => new()
    {
        ToolName = toolName,
        ArgumentPreview = argumentPreview,
        Executed = false,
        Completed = true,
        Result = new TextToolResult(notice)
    };
}
