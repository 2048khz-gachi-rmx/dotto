using Dotto.Ai.Agents.Base;
using Dotto.Discord.Services;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins the final answer composition: transcript trigger, footer placement and the
/// Discord 2000-character ceiling.
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

        var content = AgentRunMessage.Compose(
            "here you go", progress, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 1);

        content.ShouldNotContain("Tool calls");
        content.ShouldEndWith(AgentProgress.FormatFooter(2, Duration, 1));
    }

    [Test]
    public void Compose_LongRun_AppendsTheTranscript()
    {
        var progress = ProgressWithCalls(6);

        var content = AgentRunMessage.Compose(
            "done", progress, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 0);

        content.ShouldContain("Tool calls (6):");
        content.ShouldContain("- ExecuteCommand: cmd 5 // result: Exit code: 0");
    }

    [Test]
    public void Compose_FailedCall_AppendsTheTranscript()
    {
        var progress = ProgressWithCalls(1, "Exit code: 127\nsh: 1: whois: not found");

        var content = AgentRunMessage.Compose(
            "could not run whois", progress, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 0);

        content.ShouldContain("Tool calls (1):");
        content.ShouldContain("(failed)");
    }

    [Test]
    public void Compose_Verbose_ForcesTheTranscript()
    {
        var progress = ProgressWithCalls(1);

        var content = AgentRunMessage.Compose(
            "done", progress, verbose: true, transcriptTurns: 5,
            Duration, fileCount: 0);

        content.ShouldContain("Tool calls (1):");
    }

    [Test]
    public void Compose_KeepsThePromptOnTheFirstLine()
    {
        var content = AgentRunMessage.Compose(
            "answer", ProgressWithCalls(1), verbose: true, transcriptTurns: 5,
            Duration, fileCount: 0, promptLine: "-# prompt: hi");

        content.Split('\n')[0].ShouldBe("-# prompt: hi");
    }

    [Test]
    public void Compose_IncludesS3LinksBeforeTheFooter()
    {
        var content = AgentRunMessage.Compose(
            "answer", ProgressWithCalls(1), verbose: false, transcriptTurns: 5,
            Duration, fileCount: 1,
            links: ["https://s3.example/result.mp4"]);

        content.ShouldContain("-# https://s3.example/result.mp4");
        content.ShouldEndWith(AgentProgress.FormatFooter(1, Duration, 1));
    }

    [Test]
    public void Compose_TruncatesTheTranscript_ToStayWithinTheMessageLimit()
    {
        var progress = new AgentProgress(100);
        var longArgument = new string('a', 80);
        var longResult = new string('b', 200);

        for (var i = 0; i < 40; i++)
        {
            var call = progress.StartCall("ExecuteCommand", $"{longArgument} {i}");
            progress.CompleteCall(call!, longResult, failed: false);
        }

        var content = AgentRunMessage.Compose(
            new string('c', 300), progress, verbose: true, transcriptTurns: 5,
            Duration, fileCount: 0);

        content.Length.ShouldBeLessThanOrEqualTo(AgentRunMessage.DiscordMessageLimit);
        content.ShouldContain("Tool calls (40):");
        content.ShouldContain("earlier call(s) omitted");
        content.ShouldEndWith(AgentProgress.FormatFooter(40, Duration, 0));
    }

    [Test]
    public void Compose_TruncatesAnOversizedAnswer_AndStillKeepsTheFooter()
    {
        var content = AgentRunMessage.Compose(
            new string('d', 3000), progress: null, verbose: false, transcriptTurns: 5,
            Duration, fileCount: 0);

        content.Length.ShouldBeLessThanOrEqualTo(AgentRunMessage.DiscordMessageLimit);
        content.ShouldEndWith(AgentProgress.FormatFooter(0, Duration, 0));
    }

    [Test]
    public void Compose_WithoutProgress_StillWritesATranscriptFreeFooter()
    {
        var content = AgentRunMessage.Compose(
            "answer", progress: null, verbose: true, transcriptTurns: 5,
            Duration, fileCount: 2);

        content.ShouldBe($"answer\n{AgentProgress.FormatFooter(0, Duration, 2)}");
    }
}
