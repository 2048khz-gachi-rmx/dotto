using System.ComponentModel;
using System.Net.Http;
using Dotto.Ai.Abstractions;
using Dotto.Common;

namespace Dotto.Ai.Tools;

public class TerminalTools(ISandbox sandbox)
{
    [Tool(ToolAttribute.ToolTypeFlag.UserAssistant)]
    [Description("Run a terminal command in the isolated sandbox. " +
                 "Installed: ffmpeg, jq, curl, python3. " +
                 "Supports pipes, redirects, &&, and any shell syntax. " +
                 "Example: \"ls -la /work/downloads && ffprobe -v error -show_entries format=duration file.mp4\"")]
    public async Task<string> ExecuteCommand(
        [Description("Full shell command string, e.g. \"ffmpeg -i input.mp4 output.mp4\"")]
        string command,
        [Description("Working directory, defaults to /work")]
        string? cwd = null,
        [Description("Timeout tier: \"short\" (5s), \"long\" (300s), or omit for default (20s)")]
        string? timeout = null)
    {
        SandboxExecuteResult result;
        try
        {
            var container = await sandbox.EnsureStartedAsync(CancellationToken.None);
            result = await container.ExecuteAsync(command, cwd ?? "", timeout ?? "", CancellationToken.None);
        }
        catch (SandboxHttpException ex)
        {
            return ex.Message;
        }
        catch (TimeoutException ex)
        {
            return $"Sandbox timeout error: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"Sandbox communication error: {ex.Message}";
        }

        var output = string.Empty;
        if (!result.Stdout.IsNullOrEmpty())
            output += $"stdout:\n{result.Stdout}\n";
        if (!result.Stderr.IsNullOrEmpty())
            output += $"stderr:\n{result.Stderr}\n";

        return $"Exit code: {result.ExitCode}\n{output}".TrimEnd();
    }
}
