using Dotto.Common.Constants;
using Dotto.Ffmpeg.Contracts;
using Dotto.Ffmpeg.Services;
using Dotto.Ffmpeg.Settings;
using Microsoft.Extensions.Options;

namespace Dotto.Ffmpeg.Strategies;

internal class Av1CompressionStrategy : IVideoCompressorStrategy
{
    private const string Extension = ".mp4";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Constants.Compression.TempDirName);
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly CompressionSettings _settings;

    public Av1CompressionStrategy(
        FfmpegRunner ffmpegRunner,
        IOptions<CompressionSettings> settings)
    {
        _ffmpegRunner = ffmpegRunner;
        _settings = settings.Value;
    }

    public async Task<CompressionResult> CompressAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        var tempInputPath = Path.Combine(Path.GetTempPath(), $"input_{Guid.NewGuid():N}{Extension}");
        string? outputPath = null;

        try
        {
            using (var fileStream = new FileStream(tempInputPath, FileMode.CreateNew))
            {
                await input.CopyToAsync(fileStream, cancellationToken);
            }

            var strategySettings = _settings.Strategies.TryGetValue(CompressionMethod.Av1, out var settings)
                ? settings
                : new StrategySettings();

            var crf = strategySettings.Crf;
            var audioBitrateKbps = strategySettings.AudioBitrateKbps;

            Directory.CreateDirectory(_tempDir);
            outputPath = Path.Combine(_tempDir, $"out_{Guid.NewGuid():N}{Extension}");

            // https://gist.github.com/BlueSwordM/86dfcb6ab38a93a524472a0cbe4c4100
            var args = new[]
            {
                "-i", tempInputPath,
                "-c:v", "libsvtav1",
                "-c:a", "libopus",
                "-b:a", $"{audioBitrateKbps}k",
                "-crf", crf.ToString(),
                "-g", "180",
                "-svtav1-params", "fast-decode=2:tune=0:enable-overlays=1:film-grain=0:lookahead=120:scd=1",
                "-preset", "4",
                "-vf", "mpdecimate",
                "-movflags", "+faststart",
                "-pix_fmt", "yuv420p10le",
                outputPath
            };

            _ = await _ffmpegRunner.RunAsync(args, cancellationToken);

            var originalSize = new FileInfo(tempInputPath).Length;
            var compressedSize = new FileInfo(outputPath).Length;

            using var outputStream = new FileStream(outputPath, FileMode.Open, FileAccess.Read);
            var memoryStream = new MemoryStream();
            await outputStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            return new CompressionResult(
                memoryStream,
                originalSize,
                compressedSize,
                true,
                null,
                Extension);
        }
        catch (Exception ex)
        {
            return new CompressionResult(
                Stream.Null,
                0,
                0,
                false,
                ex.Message,
                Extension);
        }
        finally
        {
            DeleteFile(tempInputPath);
            if (outputPath != null)
                DeleteFile(outputPath);
        }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
