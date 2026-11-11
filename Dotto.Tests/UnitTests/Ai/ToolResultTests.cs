using Dotto.Ai.Tools;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins which stream a command's result shows: the payload on success, the explanation on
/// failure, and the other stream when the preferred one is empty.
/// </summary>
public class ToolResultTests
{
    [TestCase(0, "hello", "", "hello", TestName = "Success shows stdout")]
    [TestCase(0, "hello", "progress", "hello", TestName = "Success prefers stdout over stderr")]
    [TestCase(1, "", "boom", "boom", TestName = "Failure shows stderr")]
    [TestCase(1, "partial", "boom", "boom", TestName = "Failure prefers stderr over stdout")]
    [TestCase(1, "", "", "", TestName = "Silence stays silent")]
    [TestCase(2, "partial", "  ", "partial", TestName = "Blank preferred stream falls back")]
    public void Output_ShowsTheStreamThatMatters(int exitCode, string stdout, string stderr, string expected)
    {
        var result = new CommandToolResult("Exit code", exitCode, stdout, stderr);

        result.Output.ShouldBe(expected);
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(127, true)]
    public void Failed_FollowsTheExitStatus(int exitCode, bool expected)
    {
        new CommandToolResult("Exit code", exitCode, string.Empty, string.Empty)
            .Failed.ShouldBe(expected);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TextResults_ReportTheFailureTheyWereGiven(bool failed)
    {
        new TextToolResult("boom", failed).Failed.ShouldBe(failed);
    }
}
