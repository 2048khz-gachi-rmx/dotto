using Dotto.Ai.Agents.Base;
using Dotto.Discord.Services;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins the final answer composition: transcript trigger, footer placement, the
/// Discord 2000-character ceiling and the overflow attachment that replaces trimming.
/// </summary>
public class AgentRunMessageTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(4);

    private static AgentProgress ProgressWithCalls(int count, string result = "Exit code: 0")
    {
        var progress = new AgentProgress(100);

        for (var i = 0; i < count; i++)
        {
            var call = progress.StartCall("ExecuteCommand", $"cmd {i}");
            progress.CompleteCall(call!, result, AgentProgress.HasNonZeroExitCode(result));
        }

        return progress;
    }

    [Test]
    public void Compose_ShortSuccessfulRun_OmitsTheTranscript()
    {
        var progress = ProgressWithCalls(2);

        var composed = AgentRunMessage.Compose(
            "here you go", progress, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 1);

        composed.Content.ShouldNotContain("Tool calls");
        composed.Content.ShouldEndWith(AgentProgress.FormatFooter(2, Duration, 1));
        composed.HasOverflow.ShouldBeFalse();
    }

    [Test]
    public void Compose_LongRun_AppendsTheTranscript()
    {
        var progress = ProgressWithCalls(6);

        var composed = AgentRunMessage.Compose(
            "done", progress, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 0);

        composed.Content.ShouldContain("Tool calls (6):");
        composed.Content.ShouldContain("- ExecuteCommand: cmd 5");
        composed.HasOverflow.ShouldBeFalse();
    }

    [Test]
    public void Compose_FailedCallsAlone_DoNotAppendTheTranscript()
    {
        var progress = ProgressWithCalls(1, "Exit code: 127\nsh: 1: whois: not found");

        var composed = AgentRunMessage.Compose(
            "could not run whois", progress, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 0);

        composed.Content.ShouldNotContain("Tool calls");
        composed.HasOverflow.ShouldBeFalse();
    }

    [Test]
    public void Compose_FailedCallInATranscript_IsMarked()
    {
        var progress = ProgressWithCalls(6, "Exit code: 127\nsh: 1: whois: not found");

        var composed = AgentRunMessage.Compose(
            "could not run whois", progress, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 0);

        composed.Content.ShouldContain("Tool calls (6):");
        composed.Content.ShouldContain("(failed)");
    }

    [Test]
    public void Compose_Verbose_ForcesTheTranscript()
    {
        var progress = ProgressWithCalls(1);

        var composed = AgentRunMessage.Compose(
            "done", progress, verbose: true, transcriptTurns: 5,
            Duration, fileCount: 0);

        composed.Content.ShouldContain("Tool calls (1):");
    }

    [Test]
    public void Compose_KeepsThePromptOnTheFirstLine()
    {
        var composed = AgentRunMessage.Compose(
            "answer", ProgressWithCalls(1), verbose: true, transcriptTurns: 5,
            Duration, fileCount: 0, promptLine: "-# prompt: hi");

        composed.Content.Split('\n')[0].ShouldBe("-# prompt: hi");
    }

    [Test]
    public void Compose_IncludesS3LinksBeforeTheFooter()
    {
        var composed = AgentRunMessage.Compose(
            "answer", ProgressWithCalls(1), verbose: false, transcriptTurns: 5,
            Duration, fileCount: 1,
            links: ["https://s3.example/result.mp4"]);

        composed.Content.ShouldContain("-# https://s3.example/result.mp4");
        composed.Content.ShouldEndWith(AgentProgress.FormatFooter(1, Duration, 1));
    }

    [Test]
    public void Compose_TrimsTheTranscript_ToStayWithinTheMessageLimit()
    {
        var progress = new AgentProgress(100);
        var longArgument = new string('a', 80);
        var longResult = new string('b', 200);

        for (var i = 0; i < 40; i++)
        {
            var call = progress.StartCall("ExecuteCommand", $"{longArgument} {i}");
            progress.CompleteCall(call!, longResult, failed: false);
        }

        var composed = AgentRunMessage.Compose(
            new string('c', 300), progress, verbose: true, transcriptTurns: 5,
            Duration, fileCount: 0);

        composed.Content.Length.ShouldBeLessThanOrEqualTo(AgentRunMessage.DiscordMessageLimit);
        composed.Content.ShouldContain("Tool calls (40):");
        composed.Content.ShouldContain("earlier call(s) omitted");
        composed.Content.ShouldEndWith(AgentProgress.FormatFooter(40, Duration, 0));
        composed.HasOverflow.ShouldBeFalse();
    }

    [Test]
    public void Compose_OverlongAnswer_AttachesTheFullTextInsteadOfTrimmingIt()
    {
        var answer = new string('d', 3000);

        var composed = AgentRunMessage.Compose(
            answer, progress: null, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 0, promptLine: "-# prompt: hi");

        composed.HasOverflow.ShouldBeTrue();
        composed.OverflowText.ShouldBe(answer);

        composed.Content.Length.ShouldBeLessThanOrEqualTo(AgentRunMessage.DiscordMessageLimit);
        composed.Content.Split('\n')[0].ShouldBe("-# prompt: hi");
        composed.Content.ShouldContain($"Full answer attached as {AgentRunMessage.OverflowFileName}.");
        composed.Content.ShouldEndWith(AgentProgress.FormatFooter(0, Duration, 0));
    }

    [Test]
    public void Compose_OverflowFileCarriesTheTranscript()
    {
        var composed = AgentRunMessage.Compose(
            new string('e', 2500), ProgressWithCalls(6), verbose: true, transcriptTurns: 5,
            Duration, fileCount: 0);

        composed.HasOverflow.ShouldBeTrue();
        composed.OverflowText.ShouldContain("Tool calls (6):");
        composed.Content.ShouldNotContain("Tool calls");
        composed.Content.Length.ShouldBeLessThanOrEqualTo(AgentRunMessage.DiscordMessageLimit);
    }

    [Test]
    public void Compose_OverflowAttachmentIsNamedForTheReader()
    {
        var composed = AgentRunMessage.Compose(
            new string('f', 3000), progress: null, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 0);

        var attachment = AgentRunMessage.CreateOverflowAttachment(composed);

        attachment.FileName.ShouldBe(AgentRunMessage.OverflowFileName);
    }

    [Test]
    public void Compose_WithoutProgress_StillWritesATranscriptFreeFooter()
    {
        var composed = AgentRunMessage.Compose(
            "answer", progress: null, verbose: true, transcriptTurns: 5,
            Duration, fileCount: 2);

        composed.Content.ShouldBe($"answer\n{AgentProgress.FormatFooter(0, Duration, 2)}");
        composed.HasOverflow.ShouldBeFalse();
    }
}
