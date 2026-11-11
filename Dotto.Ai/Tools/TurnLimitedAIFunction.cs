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
            return await base.InvokeCoreAsync(arguments, cancellationToken);

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
            var failureWarning = progress.CompleteCall(call, ex.Message, failed: true);
            return failureWarning.Length == 0 ? ex.Message : $"{failureWarning}\n\n{ex.Message}";
        }
        catch
        {
            progress.CompleteCall(call, null, failed: true);
            throw;
        }

        var text = result as string ?? result?.ToString();
        var warning = progress.CompleteCall(call, text, AgentProgress.HasNonZeroExitCode(text));
        if (warning.Length == 0)
            return result;

        // Warnings ride along with the result: the model never sees anything else.
        // Factory-built functions may hand back a JsonElement rather than a string,
        // so the notice always goes out as text.
        return result is null
            ? warning
            : $"{warning}\n\n{result}";
    }

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
