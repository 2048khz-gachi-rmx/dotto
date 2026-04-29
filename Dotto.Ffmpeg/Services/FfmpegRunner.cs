using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Dotto.Ffmpeg.Services;

internal sealed class FfmpegRunner
{
    private static readonly string[] noStdinArg = ["-nostdin"];

    public async Task<string> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (!args.Contains("-nostdin"))
            args = noStdinArg.Concat(args).ToArray();
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg",
                Arguments = string.Join(" ", args),
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.Start();

        var exitTask = SetupExit(process, cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var exitCode = await exitTask;
        var stderr = await stderrTask;

        if (exitCode != 0)
        {
            throw new ApplicationException($"ffmpeg exited with non-zero code ({exitCode})", new Exception(stderr));
        }

        return stderr;
    }

    static Task<int> SetupExit(Process process, CancellationToken ct)
    {
        var ecTcs = new TaskCompletionSource<int>();

        process.Exited += (_, _) =>
        {
            ecTcs.SetResult(process.ExitCode);
            process.Dispose();
        };

        ct.Register(() =>
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        });

        return ecTcs.Task;
    }
}
