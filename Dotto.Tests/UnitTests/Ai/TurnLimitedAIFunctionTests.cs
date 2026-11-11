using System.Text.Json;
using Dotto.Ai.Agents.Base;
using Dotto.Ai.Tools;
using Microsoft.Extensions.AI;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Drives a fake tool through the decorator to pin turn counting, the warning
/// thresholds, refusal past the limit and failure marking.
/// </summary>
public class TurnLimitedAIFunctionTests
{
    private static AIFunctionArguments Arguments(string command) =>
        new(new Dictionary<string, object?> { ["command"] = command });

    private static readonly AIFunctionFactoryOptions PassthroughResults = new()
    {
        MarshalResult = (value, _, _) => ValueTask.FromResult(value),
        ExcludeResultSchema = true
    };

    private static (TurnLimitedAIFunction Function, AgentProgress Progress, Func<int> Executions) Build(
        int maxTurns = 20, IToolResult? result = null)
    {
        var executions = 0;
        var reply = result ?? new TextToolResult("Exit code: 0");

        var inner = AIFunctionFactory.Create(
            (string command) =>
            {
                executions++;
                return reply;
            },
            new AIFunctionFactoryOptions
            {
                Name = "ExecuteCommand",
                MarshalResult = PassthroughResults.MarshalResult,
                ExcludeResultSchema = true
            });

        var progress = new AgentProgress(maxTurns);
        var function = new TurnLimitedAIFunction(inner, () => progress);

        return (function, progress, () => executions);
    }

    [Test]
    public async Task Invoke_WarnsWhenApproaching_AndOnTheLastTurn_AndRefusesPastTheLimit()
    {
        var (function, progress, executions) = Build(maxTurns: 20);
        var results = new List<string>();

        for (var i = 1; i <= 20; i++)
            results.Add((await function.InvokeAsync(Arguments($"whois {i}")))?.ToString() ?? "null");

        // 1-based turns: index 13 is turn 14, 14 is turn 15, 18 is turn 19.
        results[13].ShouldNotContain(AgentProgress.ApproachingLimitWarning);
        results[14].ShouldContain(AgentProgress.ApproachingLimitWarning);
        results[18].ShouldContain(AgentProgress.LastTurnWarning);
        results.ShouldAllBe(result => result.Contains("Exit code: 0"));

        var refused = (await function.InvokeAsync(Arguments("whois 21")))?.ToString();

        refused.ShouldBe(AgentProgress.TurnLimitNotice);
        executions().ShouldBe(20);
        progress.Turns.ShouldBe(20);
        progress.RefusedCalls.ShouldBe(1);
        progress.Warnings.Count.ShouldBe(2);
    }

    [Test]
    public async Task Invoke_HonoursAConfiguredLimit()
    {
        var (function, progress, executions) = Build(maxTurns: 3);

        for (var i = 0; i < 3; i++)
            await function.InvokeAsync(Arguments($"cmd {i}"));

        var refused = (await function.InvokeAsync(Arguments("cmd 4")))?.ToString();

        refused.ShouldBe(AgentProgress.TurnLimitNotice);
        executions().ShouldBe(3);
        progress.Turns.ShouldBe(3);
        progress.RefusedCalls.ShouldBe(1);
    }

    [Test]
    public async Task Invoke_RecordsPreviews_WithoutTheWarning()
    {
        var (function, progress, _) = Build(maxTurns: 20);

        for (var i = 1; i <= 15; i++)
            await function.InvokeAsync(Arguments($"whois {i}"));

        var call = progress.Calls[14];
        call.ArgumentPreview.ShouldBe("whois 15");
        call.Result.ShouldBeOfType<TextToolResult>().Text.ShouldBe("Exit code: 0");
        call.Completed.ShouldBeTrue();
    }

    [Test]
    public async Task Invoke_MarksNonZeroExitAsFailed()
    {
        var (function, progress, _) = Build(
            result: new CommandToolResult(
                "Exit code: 127\nstderr:\nsh: 1: whois: not found\n",
                127,
                string.Empty,
                "sh: 1: whois: not found"));

        await function.InvokeAsync(Arguments("whois -h whois.radb.net"));

        var call = progress.Calls[0];
        call.Failed.ShouldBeTrue();

        var result = call.Result.ShouldBeOfType<CommandToolResult>();
        result.ExitCode.ShouldBe(127);
        result.Output.ShouldBe("sh: 1: whois: not found");
    }

    [Test]
    public async Task Invoke_TurnsAToolFailureExceptionIntoAResultTheModelSees()
    {
        var inner = AIFunctionFactory.Create(
            (Func<string, string>)(path => throw new ToolFailureException("File not found: `/work/nope.mp4`.")),
            name: "UploadFile",
            serializerOptions: JsonSerializerOptions.Default);
        var progress = new AgentProgress(20);
        var function = new TurnLimitedAIFunction(inner, () => progress);

        var result = await function.InvokeAsync(new AIFunctionArguments(
            new Dictionary<string, object?> { ["path"] = "/work/nope.mp4" }));

        result?.ToString().ShouldBe("File not found: `/work/nope.mp4`.");
        progress.Calls[0].Failed.ShouldBeTrue();
        progress.Calls[0].Result.ShouldBeOfType<TextToolResult>().Failed.ShouldBeTrue();
        progress.Turns.ShouldBe(1);
    }

    [Test]
    public async Task Invoke_TypedResultsNeverReachTheModelAsObjects()
    {
        var (function, _, _) = Build(
            result: new CommandToolResult("Exit code: 0\nstdout:\nhello\n", 0, "hello", string.Empty));

        var result = await function.InvokeAsync(Arguments("echo hello"));

        result.ShouldBeOfType<string>().ShouldBe("Exit code: 0\nstdout:\nhello\n");
    }

    [Test]
    public async Task Invoke_DoesNotTreatErrorWordsInASuccessfulResultAsFailure()
    {
        var (function, progress, _) = Build(
            result: new TextToolResult("stdout: 0 errors, 3 files not found in index"));

        await function.InvokeAsync(Arguments("grep -c error log.txt"));

        progress.Calls[0].Failed.ShouldBeFalse();
    }

    [Test]
    public void BuildArgumentPreview_UsesTheFirstNonEmptyArgument()
    {
        var preview = TurnLimitedAIFunction.BuildArgumentPreview(new AIFunctionArguments(
            new Dictionary<string, object?>
            {
                ["cwd"] = "",
                ["command"] = "ffmpeg -i in.mp4 out.mp4",
                ["timeout"] = "long"
            }));

        preview.ShouldBe("ffmpeg -i in.mp4 out.mp4");
    }

    [Test]
    public void BuildArgumentPreview_TruncatesLongCommands()
    {
        var preview = TurnLimitedAIFunction.BuildArgumentPreview(Arguments(new string('x', 400)));

        preview.Length.ShouldBe(AgentProgress.ArgumentPreviewLength + 1);
        preview.ShouldEndWith(AgentProgress.TruncationMarker.ToString());
    }
}
