using Dotto.Common.Constants;
using Dotto.Ffmpeg.Contracts;
using Dotto.Ffmpeg.Services;
using Dotto.Ffmpeg.Settings;
using Microsoft.Extensions.Options;

namespace Dotto.Ffmpeg.Strategies;

internal class Vp9CompressionStrategy : IVideoCompressorStrategy
{
    private const string Extension = ".webm";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Constants.Compression.TempDirName);
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly CompressionSettings _settings;

    public Vp9CompressionStrategy(
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
        string? logFileBase = null;

        try
        {
            using (var fileStream = new FileStream(tempInputPath, FileMode.CreateNew))
            {
                await input.CopyToAsync(fileStream, cancellationToken);
            }

            var strategySettings = _settings.Strategies.TryGetValue(CompressionMethod.Vp9, out var settings)
                ? settings
                : new StrategySettings();

            var crf = strategySettings.Crf;
            var audioBitrateKbps = strategySettings.AudioBitrateKbps;

            Directory.CreateDirectory(_tempDir);
            logFileBase = Path.Combine(_tempDir, $"ffmpeg_{Guid.NewGuid():N}");

            // Pass 1: analyze
            var pass1Args = new[]
            {
                "-i", tempInputPath,
                "-c:v", "libvpx-vp9",
                "-b:v", "0",
                "-row-mt", "1",
                "-crf", crf.ToString(),
                "-passlogfile", logFileBase,
                "-pass", "1",
                "-an",
                "-f", "null",
                "-"
            };
            _ = await _ffmpegRunner.RunAsync(pass1Args, cancellationToken);

            // Pass 2: encode
            var uuid = Guid.NewGuid().ToString("N");
            outputPath = Path.Combine(_tempDir, $"out_{uuid}.webm");

            var pass2Args = new[]
            {
                "-i", tempInputPath,
                "-c:v", "libvpx-vp9",
                "-b:v", "0",
                "-row-mt", "1",
                "-crf", crf.ToString(),
                "-passlogfile", logFileBase,
                "-pass", "2",
                "-c:a", "libopus",
                "-b:a", $"{audioBitrateKbps}k",
                "-speed", "2",
                "-f", "webm",
                outputPath
            };
            _ = await _ffmpegRunner.RunAsync(pass2Args, cancellationToken);

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
            if (logFileBase != null)
                DeleteFile($"{logFileBase}-0.log");
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
