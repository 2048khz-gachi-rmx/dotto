namespace Dotto.Ai.Tools;

/// <summary>
/// What a tool hands back to the run: the text the model reads, plus whatever the recorder and
/// the activity renderer need to describe the outcome. A result describes itself; it never
/// writes into the run's bookkeeping.
/// </summary>
public interface IToolResult
{
    /// <summary>The text handed to the model. The only thing every tool must provide.</summary>
    string Text { get; }

    /// <summary>Whether the tool considers the outcome a failure (non-zero exit, tool error).</summary>
    bool Failed => false;
}

/// <summary>
/// A tool whose outcome is just text: downloads, uploads, file reads.
/// </summary>
public sealed record TextToolResult(string Text, bool Failed = false) : IToolResult;

/// <summary>
/// A sandbox command: the model-facing text plus the exit status and both output streams, so the
/// activity line can show the status and the stream that explains it.
/// </summary>
public sealed record CommandToolResult(string Text, int ExitCode, string Stdout, string Stderr)
    : IToolResult
{
    public bool Failed => ExitCode != 0;

    /// <summary>
    /// The stream worth showing: on success the payload (stdout), on failure the explanation
    /// (stderr), falling back to the other when the preferred one is empty.
    /// </summary>
    public string Output => ExitCode != 0
        ? FirstNonEmpty(Stderr, Stdout)
        : FirstNonEmpty(Stdout, Stderr);

    private static string FirstNonEmpty(string preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}
