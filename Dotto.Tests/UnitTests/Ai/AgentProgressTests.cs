using Dotto.Ai.Agents.Base;
using Dotto.Ai.Tools;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins the per-run tool-call record: previews, activity lines, failure detection,
/// transcript trigger and footer text.
/// </summary>
public class AgentProgressTests
{
    [Test]
    public void Preview_ShortText_IsUnchanged()
    {
        AgentProgress.Preview("ffmpeg -i in.mp4 out.mp4").ShouldBe("ffmpeg -i in.mp4 out.mp4");
    }

    [Test]
    public void Preview_LongText_TruncatesToTheArgumentBudgetPlusMarker()
    {
        var preview = AgentProgress.Preview(new string('a', 1200));

        preview.Length.ShouldBe(AgentProgress.ArgumentPreviewLength + 1);
        preview.ShouldEndWith(AgentProgress.TruncationMarker.ToString());
    }

    [Test]
    public void Preview_MultilineOutput_IsCollapsedToASingleLine()
    {
        AgentProgress.Preview("Exit code: 127\nsh: 1: whois: not found")
            .ShouldBe("Exit code: 127 sh: 1: whois: not found");
    }

    [Test]
    public void RenderLine_SilentSuccess_HasNoResultPart()
    {
        var progress = new AgentProgress(20);
        var call = progress.StartCall("ExecuteCommand", "true");
        progress.CompleteCall(call!, new CommandToolResult("Exit code: 0", 0, string.Empty, string.Empty));

        AgentProgress.RenderLine(call!).ShouldBe("-# ExecuteCommand: `true`");
    }

    [Test]
    public void RenderLine_TextResult_IsShownWithoutAStatus()
    {
        var progress = new AgentProgress(20);
        var call = progress.StartCall("DownloadMedia", "https://example.com/a.mp4");
        progress.CompleteCall(call!, new TextToolResult("Downloaded to /work/downloads/a.mp4"));

        AgentProgress.RenderLine(call!).ShouldBe(
            "-# DownloadMedia: `https://example.com/a.mp4` **#>** `Downloaded to /work/downloads/a.mp4`");
    }

    [Test]
    public void RenderLine_LongOutput_IsTruncatedToTheResultBudget()
    {
        var progress = new AgentProgress(20);
        var call = progress.StartCall("ExecuteCommand", "cat big.txt");
        progress.CompleteCall(
            call!,
            new CommandToolResult("Exit code: 0", 0, new string('z', 900), string.Empty));

        var line = AgentProgress.RenderLine(call!);

        line.ShouldEndWith("…`");
        line.Length.ShouldBeLessThan(AgentProgress.ResultPreviewLength + 100);
    }

    [Test]
    public void Preview_WhitespaceOnly_IsEmpty()
    {
        AgentProgress.Preview(" \n\t ").ShouldBeEmpty();
    }

    [Test]
    public void RenderLine_PendingCall_OmitsTheResultPart()
    {
        var progress = new AgentProgress(20);
        var call = progress.StartCall("ExecuteCommand", "whois -h whois.radb.net");

        AgentProgress.RenderLine(call!).ShouldBe("-# ExecuteCommand: `whois -h whois.radb.net`");
    }

    [Test]
    public void RenderLine_CompletedCall_IncludesTheResultPart()
    {
        var progress = new AgentProgress(20);
        var call = progress.StartCall("ExecuteCommand", "whois -h whois.radb.net");
        progress.CompleteCall(
            call!,
            new CommandToolResult(
                "Exit code: 127\nstderr:\nsh: 1: whois: not found\n",
                127,
                string.Empty,
                "sh: 1: whois: not found"));

        AgentProgress.RenderLine(call!).ShouldBe(
            "-# ExecuteCommand: `whois -h whois.radb.net` **#>** [127] `sh: 1: whois: not found`");
    }

    [Test]
    public void RenderLine_SuccessfulCall_OmitsTheExitCode()
    {
        var progress = new AgentProgress(20);
        var call = progress.StartCall("ExecuteCommand", "echo helloooo");
        progress.CompleteCall(
            call!,
            new CommandToolResult("Exit code: 0\nstdout:\nhelloooo\n", 0, "helloooo", string.Empty));

        AgentProgress.RenderLine(call!).ShouldBe(
            "-# ExecuteCommand: `echo helloooo` **#>** `helloooo`");
    }

    [Test]
    public void RenderLine_BackticksInContent_DoNotBreakTheCodeSpan()
    {
        var progress = new AgentProgress(20);
        var call = progress.StartCall("ExecuteCommand", "echo `date`");
        progress.CompleteCall(
            call!,
            new CommandToolResult("Exit code: 0\nstdout:\n`date`\n", 0, "`date`", string.Empty));

        var line = AgentProgress.RenderLine(call!);

        line.Count(c => c == '`').ShouldBe(4);
        line.ShouldContain("`echo ˋdateˋ`");
    }

    [Test]
    public void RenderActivity_ShowsOnlyTheNewestCalls()
    {
        var progress = new AgentProgress(20);
        for (var i = 0; i < 8; i++)
            progress.StartCall("ExecuteCommand", $"cmd {i}");

        var activity = AgentProgress.RenderActivity(progress.Calls);

        activity.Split('\n').Length.ShouldBe(AgentProgress.ActivityLines);
        activity.ShouldContain("cmd 7");
        activity.ShouldNotContain("cmd 2");
    }

    [Test]
    public void Footer_ReportsTurnsDurationAndFiles()
    {
        var progress = new AgentProgress(20);

        progress.BeginTurn();
        progress.StartCall("ExecuteCommand", "one");
        progress.StartCall("ExecuteCommand", "two");
        progress.BeginTurn();
        progress.StartCall("ExecuteCommand", "three");

        progress.BuildFooter(TimeSpan.FromSeconds(12.6), fileCount: 1)
            .ShouldBe("-# 2 turns · 13s · 1 file(s)");
    }

    [TestCase(4, false, false)]
    [TestCase(5, false, true)]
    [TestCase(1, true, true)]
    public void ShouldShowTranscript_FollowsTheTriggerRules(
        int turns, bool verbose, bool expected)
    {
        var progress = new AgentProgress(20);
        for (var i = 0; i < turns; i++)
        {
            progress.BeginTurn();
            progress.StartCall("ExecuteCommand", "cmd");
        }

        progress.ShouldShowTranscript(transcriptTurns: 5, verbose).ShouldBe(expected);
    }

    [TestCase(6, false)]
    [TestCase(19, false)]
    [TestCase(20, true)]
    public void ShouldShowTranscript_UnsetThreshold_UsesTheTurnLimit(int turns, bool expected)
    {
        var progress = new AgentProgress(20);
        for (var i = 0; i < turns; i++)
        {
            progress.BeginTurn();
            progress.StartCall("ExecuteCommand", "cmd");
        }

        progress.ShouldShowTranscript(transcriptTurns: null, verbose: false).ShouldBe(expected);
    }

    [Test]
    public void ShouldShowTranscript_IsFalse_WhenOnlyAFewCallsFailed()
    {
        // Failing commands are ordinary; a short run does not dump its transcript for them.
        var progress = new AgentProgress(20);
        var call = progress.StartCall("ExecuteCommand", "cmd");
        progress.CompleteCall(call!, new TextToolResult("Exit code: 1", Failed: true));

        progress.ShouldShowTranscript(transcriptTurns: 5, verbose: false).ShouldBeFalse();
    }

    [Test]
    public void Failed_FollowsTheResult()
    {
        var progress = new AgentProgress(20);

        var success = progress.StartCall("ExecuteCommand", "true");
        progress.CompleteCall(success!, new CommandToolResult("Exit code: 0", 0, string.Empty, string.Empty));

        var failure = progress.StartCall("ExecuteCommand", "false");
        progress.CompleteCall(failure!, new CommandToolResult("Exit code: 1", 1, string.Empty, "boom"));

        var toolError = progress.StartCall("ReadFile", "/nope");
        progress.CompleteCall(toolError!, new TextToolResult("File not found", Failed: true));

        success!.Failed.ShouldBeFalse();
        failure!.Failed.ShouldBeTrue();
        toolError!.Failed.ShouldBeTrue();
    }

    [Test]
    public void TextResultsAreNotFailures_JustBecauseTheirTextMentionsErrors()
    {
        var progress = new AgentProgress(20);
        var call = progress.StartCall("DownloadMedia", "https://example.com/a.mp4");
        progress.CompleteCall(call!, new TextToolResult("Failed to download media: 0 errors reported"));

        call!.Failed.ShouldBeFalse();
    }

    [Test]
    public void BuildTranscript_ListsNewestCalls_AndCountsOmittedOnes()
    {
        var progress = new AgentProgress(20);
        for (var i = 0; i < 10; i++)
        {
            var call = progress.StartCall("ExecuteCommand", $"cmd {i}");
            progress.CompleteCall(call!, new TextToolResult("Exit code: 0"));
        }

        var transcript = progress.BuildTranscript(maxChars: 240);

        transcript.ShouldContain("Tool calls (10):");
        transcript.ShouldContain("earlier call(s) omitted");
        transcript.ShouldContain("cmd 9");
        transcript.ShouldNotContain("cmd 0");
        transcript.Length.ShouldBeLessThanOrEqualTo(260);
    }

    [Test]
    public void BuildTranscript_EmptyWhenThereAreNoCalls()
    {
        new AgentProgress(20).BuildTranscript().ShouldBeEmpty();
    }
}
