using System.Diagnostics;
using System.Runtime.InteropServices;
using Dotto.Common.Constants;

namespace Dotto.Ffmpeg.Services;

internal class FfmpegService
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Constants.FfmpegTemp.DirName);

    public async Task<(string OutputPath, long OriginalSize)> CompressVp9Async(
        string inputPath,
        int crf,
        int audioBitrateKbps,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_tempDir);
        var uuid = Guid.NewGuid().ToString("N");
        var logFileBase = Path.Combine(_tempDir, $"ffmpeg_{uuid}");

        try
        {
            await RunPass1Async(inputPath, logFileBase, crf, cancellationToken);
            return await RunPass2Async(inputPath, logFileBase, crf, audioBitrateKbps, cancellationToken);
        }
        finally
        {
            DeleteLogFile($"{logFileBase}-0.log");
        }
    }

    private async Task RunPass1Async(string inputPath, string logFileBase, int crf, CancellationToken cancellationToken)
    {
        var args = $"-i \"{inputPath}\" -c:v libvpx-vp9 -b:v 0 -row-mt 1 -crf {crf} -passlogfile \"{logFileBase}\" -pass 1 -an -f null -";

        await RunFfmpegAsync(args, cancellationToken);
    }

    private async Task<(string OutputPath, long OriginalSize)> RunPass2Async(
        string inputPath,
        string logFileBase,
        int crf,
        int audioBitrateKbps,
        CancellationToken cancellationToken)
    {
        var uuid = Guid.NewGuid().ToString("N");
        var outputPath = Path.Combine(_tempDir, $"out_{uuid}.webm");
        var args = $"-i \"{inputPath}\" -c:v libvpx-vp9 -b:v 0 -row-mt 1 -crf {crf} -passlogfile \"{logFileBase}\" -pass 2 -c:a libopus -b:a {audioBitrateKbps}k -speed 2 -f webm \"{outputPath}\"";

        var originalSize = new FileInfo(inputPath).Length;

        await RunFfmpegAsync(args, cancellationToken);

        return (outputPath, originalSize);
    }

    private async Task RunFfmpegAsync(string args, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg",
                Arguments = args,
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

        if (exitCode != 0)
        {
            var stderr = await stderrTask;
            throw new ApplicationException($"ffmpeg exited with non-zero code ({exitCode})", new Exception(stderr));
        }
    }

    private static Task<int> SetupExit(Process process, CancellationToken ct)
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

    private void DeleteLogFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
