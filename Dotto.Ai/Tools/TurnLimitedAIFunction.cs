using System.Globalization;
using Dotto.Ai.Agents.Base;
using Microsoft.Extensions.AI;

namespace Dotto.Ai.Tools;

/// <summary>
/// Wraps a registered tool so the run can observe calls and enforce the turn budget,
/// without any tool knowing about it. One tool call is one turn.
/// </summary>
internal sealed class TurnLimitedAIFunction(
    AIFunction innerFunction,
    Func<AgentProgress?> progressAccessor) : DelegatingAIFunction(innerFunction)
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var progress = progressAccessor();
        if (progress is null)
            return AsModelText(await base.InvokeCoreAsync(arguments, cancellationToken));

        var call = progress.StartCall(Name, BuildArgumentPreview(arguments));
        if (call is null)
            return AgentProgress.TurnLimitNotice;

        object? result;
        try
        {
            result = await base.InvokeCoreAsync(arguments, cancellationToken);
        }
        catch (ToolFailureException ex)
        {
            // A tool-reported failure is a normal, model-visible outcome: record it and
            // hand the same text back as the result instead of surfacing an exception.
            var failureWarning = progress.CompleteCall(call, new TextToolResult(ex.Message, Failed: true));
            return Compose(failureWarning, ex.Message);
        }
        catch (Exception ex)
        {
            progress.CompleteCall(call, new TextToolResult(ex.Message, Failed: true));
            throw;
        }

        // Every tool hands back text; a tool with more to say (a command's exit status and
        // streams) hands back an IToolResult, which the recorder stores as-is.
        var toolResult = result switch
        {
            IToolResult typed => typed,
            null => new TextToolResult(string.Empty),
            _ => new TextToolResult(result.ToString() ?? string.Empty)
        };

        var warning = progress.CompleteCall(call, toolResult);

        // Warnings ride along with the result: the model never sees anything else.
        return Compose(warning, toolResult.Text);
    }

    /// <summary>
    /// The model must receive text, never the typed result the factory was told to pass through.
    /// </summary>
    private static object? AsModelText(object? result) =>
        result is IToolResult toolResult ? toolResult.Text : result;

    private static string Compose(string warning, string text) =>
        warning.Length == 0 ? text : $"{warning}\n\n{text}";

    /// <summary>
    /// The first non-empty argument is what the activity line shows (a command, URL, path);
    /// the rest are options and would only crowd out the useful part.
    /// </summary>
    internal static string BuildArgumentPreview(AIFunctionArguments arguments)
    {
        foreach (var (_, value) in arguments)
        {
            var text = value switch
            {
                null => null,
                string s => s,
                bool b => b ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };

            if (!string.IsNullOrWhiteSpace(text))
                return AgentProgress.Preview(text);
        }

        return string.Empty;
    }
}
