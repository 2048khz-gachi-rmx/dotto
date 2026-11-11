using System.Text;
using Dotto.Ai.Agents.Base;
using NetCord.Rest;

namespace Dotto.Discord.Services;

/// <summary>
/// Composes the final answer for an agent run: the answer text, an optional tool
/// transcript, attachment links and the run footer. When the result would exceed
/// Discord's message limit, the full text is attached as a file and the message keeps
/// the prompt line plus a readable excerpt.
/// </summary>
public static class AgentRunMessage
{
    public const int DiscordMessageLimit = 2000;

    /// <summary>Name of the attachment carrying the full answer when the message overflows.</summary>
    public const string OverflowFileName = "answer.txt";

    /// <summary>Discord's per-message attachment limit.</summary>
    public const int MaxDiscordAttachments = 10;

    /// <summary>Upper bound for the transcript block, before the message limit applies.</summary>
    public const int TranscriptBudget = AgentProgress.TranscriptBudget;

    /// <summary>
    /// The message to send, plus the full text when it did not fit (to be attached as
    /// <see cref="OverflowFileName"/>).
    /// </summary>
    public sealed record ComposedRunMessage(string Content, string? OverflowText)
    {
        public bool HasOverflow => OverflowText is not null;
    }

    public static ComposedRunMessage Compose(
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
        if (content.Length <= DiscordMessageLimit)
            return new ComposedRunMessage(content, null);

        // Too long for one message: attach the whole text and show an excerpt instead.
        // The prompt line stays in the message so channel history can be reconstructed.
        // The transcript gets its full budget in the file, since no message room is needed.
        var fileTranscript = showTranscript ? progress!.BuildTranscript(TranscriptBudget) : string.Empty;
        var fullText = JoinBody(body, fileTranscript, linkBlock);
        return new ComposedRunMessage(BuildExcerpt(head, body, footer), fullText);
    }

    /// <summary>
    /// Builds the attachment carrying the full answer, when the message overflowed.
    /// </summary>
    public static AttachmentProperties CreateOverflowAttachment(ComposedRunMessage message) =>
        new(
            OverflowFileName,
            new MemoryStream(Encoding.UTF8.GetBytes(message.OverflowText ?? string.Empty)));

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

    private static string JoinBody(string body, string transcript, string linkBlock)
    {
        var builder = new StringBuilder(body);

        if (transcript.Length > 0)
        {
            if (builder.Length > 0)
                builder.Append("\n\n");

            builder.Append(transcript);
        }

        if (linkBlock.Length > 0)
        {
            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(linkBlock);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The message that accompanies the overflow attachment: prompt line, as much of the
    /// answer as fits, a note naming the attachment, and the footer.
    /// </summary>
    private static string BuildExcerpt(string head, string body, string footer)
    {
        var notice = $"-# Full answer attached as {OverflowFileName}.";
        const int separators = 4; // newlines between the four parts

        var prefix = head.TrimEnd();
        if (prefix.Length > DiscordMessageLimit - notice.Length - footer.Length - separators)
            prefix = Truncate(prefix, Math.Max(0, DiscordMessageLimit - notice.Length - footer.Length - separators - 1));

        var available = DiscordMessageLimit - prefix.Length - notice.Length - footer.Length - separators;
        var excerpt = available <= 1 ? string.Empty : Truncate(body, available);

        var parts = new List<string>(4);
        if (prefix.Length > 0)
            parts.Add(prefix);

        if (excerpt.Length > 0)
            parts.Add(excerpt);

        parts.Add(notice);
        parts.Add(footer);

        var content = string.Join('\n', parts);
        if (content.Length <= DiscordMessageLimit)
            return content;

        // Last resort: keep the footer, cut whatever is still too long.
        var tail = notice + "\n" + footer;
        var allowed = Math.Max(0, DiscordMessageLimit - tail.Length - 2);
        var cut = Truncate(string.Join('\n', parts.Take(parts.Count - 2)), allowed);

        return cut.Length > 0 ? cut + "\n" + tail : tail;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength
            ? text
            : text[..maxLength].TrimEnd() + "…";
}
