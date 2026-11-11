using System.ComponentModel;
using System.Net.Http;
using Dotto.Ai.Sandbox;
using Dotto.Common;
using Microsoft.Extensions.Logging;

namespace Dotto.Ai.Tools;

public class TerminalTools(ISandbox sandbox, ILogger<TerminalTools>? logger = null)
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
        ISandboxContainer container;
        try
        {
            container = await sandbox.EnsureStartedAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Sandbox failed to start for ExecuteCommand");
            return SandboxToolMessages.StartFailed;
        }

        try
        {
            result = await container.ExecuteAsync(command, cwd ?? "", timeout ?? "", CancellationToken.None);
        }
        catch (SandboxHttpException ex)
        {
            logger?.LogWarning(ex, "Sandbox rejected command execution");
            return ex.Message;
        }
        catch (TimeoutException ex)
        {
            logger?.LogWarning(ex, "Sandbox HTTP request timed out");
            return $"Sandbox timeout error: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning(ex, "Sandbox communication failed");
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
