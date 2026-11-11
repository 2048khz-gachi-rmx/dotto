using System.Text;
using Dotto.Ai.Agents.Base;

namespace Dotto.Discord.Services;

/// <summary>
/// Composes the final answer for an agent run: the answer text, an optional tool
/// transcript, attachment links and the run footer. Transcript and body are trimmed
/// so the message never exceeds Discord's 2000-character limit.
/// </summary>
public static class AgentRunMessage
{
    public const int DiscordMessageLimit = 2000;

    /// <summary>Upper bound for the transcript block, before the message limit applies.</summary>
    public const int TranscriptBudget = AgentProgress.TranscriptBudget;

    public static string Compose(
        string? answer,
        AgentProgress? progress,
        bool verbose,
        int transcriptTurns,
        TimeSpan duration,
        int fileCount,
        string? promptLine = null,
        IReadOnlyList<string>? links = null)
    {
        var head = string.IsNullOrEmpty(promptLine) ? string.Empty : promptLine + "\n";
        var body = answer?.TrimEnd() ?? string.Empty;
        var linkBlock = links is { Count: > 0 }
            ? string.Join('\n', links.Select(link => "-# " + link))
            : string.Empty;
        var footer = AgentProgress.FormatFooter(progress?.Turns ?? 0, duration, fileCount);

        var showTranscript = progress != null &&
                             progress.ShouldShowTranscript(transcriptTurns, verbose);

        var transcript = string.Empty;
        if (showTranscript)
        {
            var overhead = head.Length
                           + body.Length
                           + (linkBlock.Length > 0 ? linkBlock.Length + 1 : 0)
                           + footer.Length
                           + 2;

            var budget = Math.Min(TranscriptBudget, Math.Max(0, DiscordMessageLimit - overhead));
            transcript = progress!.BuildTranscript(budget);
        }

        var content = Join(head + body, transcript, linkBlock, footer);

        // The transcript absorbs the overflow first, then the answer body.
        if (content.Length > DiscordMessageLimit && transcript.Length > 0)
        {
            var overflow = content.Length - DiscordMessageLimit;
            transcript = progress!.BuildTranscript(Math.Max(0, transcript.Length - overflow - 24));
            content = Join(head + body, transcript, linkBlock, footer);
        }

        if (content.Length > DiscordMessageLimit && body.Length > 0)
        {
            var overflow = content.Length - DiscordMessageLimit + 1; // room for the marker
            body = overflow >= body.Length
                ? string.Empty
                : body[..(body.Length - overflow)].TrimEnd() + "…";

            content = Join(head + body, transcript, linkBlock, footer);
        }

        if (content.Length > DiscordMessageLimit && transcript.Length > 0)
        {
            transcript = string.Empty;
            content = Join(head + body, transcript, linkBlock, footer);
        }

        if (content.Length > DiscordMessageLimit)
            content = HardCap(head + body, linkBlock, footer);

        return content;
    }

    private static string Join(string prefix, string transcript, string linkBlock, string footer)
    {
        var builder = new StringBuilder(prefix);

        if (transcript.Length > 0)
            builder.Append("\n\n").Append(transcript);

        if (linkBlock.Length > 0)
            builder.Append('\n').Append(linkBlock);

        if (builder.Length > 0)
            builder.Append('\n');

        return builder.Append(footer).ToString();
    }

    /// <summary>
    /// Last resort: cut the answer itself but always keep the footer on the last line.
    /// </summary>
    private static string HardCap(string prefix, string linkBlock, string footer)
    {
        var tail = linkBlock.Length > 0 ? linkBlock + "\n" : string.Empty;
        var allowed = DiscordMessageLimit - footer.Length - tail.Length - 2;

        if (allowed < 0)
            return footer;

        if (prefix.Length > allowed)
            prefix = prefix[..allowed].TrimEnd() + "…";

        var builder = new StringBuilder(prefix);
        if (builder.Length > 0)
            builder.Append('\n');

        if (tail.Length > 0)
            builder.Append(tail);

        return builder.Append(footer).ToString();
    }
}
