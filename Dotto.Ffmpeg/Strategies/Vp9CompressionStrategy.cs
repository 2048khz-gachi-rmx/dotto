using Dotto.Ffmpeg.Contracts;
using Dotto.Ffmpeg.Services;
using Dotto.Ffmpeg.Settings;
using Microsoft.Extensions.Options;

namespace Dotto.Ffmpeg.Strategies;

 internal class Vp9CompressionStrategy : IVideoCompressorStrategy
{
    private const string Extension = ".webm";

    private readonly FfmpegService _ffmpegService;
    private readonly CompressionSettings _settings;

    public Vp9CompressionStrategy(
        FfmpegService ffmpegService,
        IOptions<CompressionSettings> settings)
    {
        _ffmpegService = ffmpegService;
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

            var strategySettings = _settings.Strategies.TryGetValue(CompressionMethod.Vp9, out var settings)
                ? settings
                : new StrategySettings();

            (outputPath, var originalSize) = await _ffmpegService.CompressVp9Async(
                tempInputPath,
                strategySettings.Crf,
                strategySettings.AudioBitrateKbps,
                cancellationToken);

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

    static void DeleteFile(string path)
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
