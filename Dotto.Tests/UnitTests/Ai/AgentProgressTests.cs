using Dotto.Ai.Agents.Base;
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
    public void PreviewResult_PrefersStderrAndPrefixesTheExitCode()
    {
        var result = "Exit code: 1\nstdout:\npartial output\nstderr:\npermission denied\n";

        AgentProgress.PreviewResult(result).ShouldBe("Exit code: 1 permission denied");
    }

    [Test]
    public void PreviewResult_FallsBackToStdout_WhenStderrIsEmpty()
    {
        var result = "Exit code: 0\nstdout:\nhello world\n";

        AgentProgress.PreviewResult(result).ShouldBe("hello world");
    }

    [Test]
    public void PreviewResult_SilentSuccess_HasNoResultPart()
    {
        AgentProgress.PreviewResult("Exit code: 0\n").ShouldBeEmpty();
        AgentProgress.PreviewResult(null).ShouldBeEmpty();
    }

    [Test]
    public void PreviewResult_TruncatesThePayloadToTheResultBudget()
    {
        var result = $"Exit code: 0\nstdout:\n{new string('z', 900)}\n";

        var preview = AgentProgress.PreviewResult(result);

        preview.Length.ShouldBe(AgentProgress.ResultPreviewLength + 1);
        preview.ShouldEndWith(AgentProgress.TruncationMarker.ToString());
    }

    [Test]
    public void PreviewResult_NonShellResult_IsPassedThrough()
    {
        AgentProgress.PreviewResult("File not found: `/work/nope.mp4`.")
            .ShouldBe("File not found: `/work/nope.mp4`.");
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

        AgentProgress.RenderLine(call!).ShouldBe("-# ExecuteCommand: whois -h whois.radb.net");
    }

    [Test]
    public void RenderLine_CompletedCall_IncludesTheResultPart()
    {
        var progress = new AgentProgress(20);
        var call = progress.StartCall("ExecuteCommand", "whois -h whois.radb.net");
        progress.CompleteCall(call!, "Exit code: 127\nsh: 1: whois: not found", failed: true);

        AgentProgress.RenderLine(call!).ShouldBe(
            "-# ExecuteCommand: whois -h whois.radb.net #-> result: Exit code: 127 sh: 1: whois: not found");
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
        progress.StartCall("ExecuteCommand", "one");
        progress.StartCall("ExecuteCommand", "two");

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
            progress.StartCall("ExecuteCommand", "cmd");

        progress.ShouldShowTranscript(transcriptTurns: 5, verbose).ShouldBe(expected);
    }

    [Test]
    public void ShouldShowTranscript_IsFalse_WhenOnlyAFewCallsFailed()
    {
        // Failing commands are ordinary; a short run does not dump its transcript for them.
        var progress = new AgentProgress(20);
        var call = progress.StartCall("ExecuteCommand", "cmd");
        progress.CompleteCall(call!, "Exit code: 1", failed: true);

        progress.ShouldShowTranscript(transcriptTurns: 5, verbose: false).ShouldBeFalse();
    }

    [Test]
    public void HasNonZeroExitCode_TreatsNonZeroExitAsFailure()
    {
        AgentProgress.HasNonZeroExitCode("Exit code: 127\nsh: 1: whois: not found").ShouldBeTrue();
    }

    [Test]
    public void HasNonZeroExitCode_TreatsZeroExitAsSuccess()
    {
        AgentProgress.HasNonZeroExitCode("Exit code: 0\nstdout:\nok").ShouldBeFalse();
    }

    [Test]
    public void HasNonZeroExitCode_IgnoresErrorMessageText()
    {
        // Failure is structural (exception or exit status), never word-matching: a
        // successful tool whose output happens to mention errors is not a failure.
        AgentProgress.HasNonZeroExitCode("Failed to download media from the provided URL.").ShouldBeFalse();
        AgentProgress.HasNonZeroExitCode("File not found: `/work/nope.mp4`.").ShouldBeFalse();
        AgentProgress.HasNonZeroExitCode(null).ShouldBeFalse();
        AgentProgress.HasNonZeroExitCode("stdout: 0 errors").ShouldBeFalse();
    }

    [Test]
    public void BuildTranscript_ListsNewestCalls_AndCountsOmittedOnes()
    {
        var progress = new AgentProgress(20);
        for (var i = 0; i < 10; i++)
        {
            var call = progress.StartCall("ExecuteCommand", $"cmd {i}");
            progress.CompleteCall(call!, "Exit code: 0", failed: false);
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
