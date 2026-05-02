using Dotto.Common.Constants;
using Microsoft.Extensions.Hosting;

namespace Dotto.Ffmpeg.Services;

internal class FfmpegTempCleanupService : IHostedService
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Constants.Compression.TempDirName);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        CleanupStaleFiles();
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        CleanupStaleFiles();
        return Task.CompletedTask;
    }

    void CleanupStaleFiles()
    {
        if (!Directory.Exists(_tempDir))
            return;

        foreach (var file in Directory.EnumerateFiles(_tempDir))
        {
            var name = Path.GetFileName(file);
            if (!IsDottoFile(name))
                continue;

            try
            {
                File.Delete(file);
            }
            catch
            {
                // Ignore cleanup errors — file may be locked by an in-progress operation
            }
        }
    }

    // Valid patterns: ffmpeg_{guid}[-0.log], out_{guid}.webm, out_{guid}.mp4
    static bool IsDottoFile(string name)
    {
        if (name.StartsWith("ffmpeg_") && Guid.TryParse(name[7..], out _))
            return true;

        if (name.StartsWith("out_") && (name.EndsWith(".webm") || name.EndsWith(".mp4")))
        {
            var fileNameOnly = Path.GetFileNameWithoutExtension(name);
            var isGuid = Guid.TryParse(fileNameOnly[4..], out _);
            return isGuid;
        }

        return false;
    }
}
