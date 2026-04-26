using Dotto.Application.Abstractions.VideoProcessing;
using Dotto.Common;
using Dotto.Discord.Models.Compress;
using Dotto.Ffmpeg.Contracts;
using Dotto.Ffmpeg.Settings;
using Microsoft.Extensions.Options;
using NetCord.Rest;

namespace Dotto.Discord.CommandHandlers.Compress;

internal class CompressCommandHandler(
    IVideoCompressionService compressionService,
    IOptions<CompressionSettings> settings)
{
    private readonly CompressionSettings _settings = settings.Value;

    public async Task<CompressMediaResult<T>> CreateMessage<T>(
        List<(Uri Url, string Name)> videos,
        CompressionMethod method,
        bool applyThresholds,
        CancellationToken ct = default)
        where T : IMessageProperties, new()
    {
        var results = new List<(CompressionResult Result, string OriginalName)>();
        var message = new T();

        foreach (var (url, name) in videos)
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await compressionService.CompressVideoAsync(
                stream,
                new CompressionOptions(method),
                ct);
            
            results.Add((result, name));
        }

        long oldTotal = 0;
        long newTotal = 0;
        var attachments = new List<AttachmentProperties>();

        foreach (var (result, originalName) in results)
        {
            if (!result.Success) continue;
            
            oldTotal += result.OriginalSize;
            newTotal += result.CompressedSize;
            
            var outputName = Path.GetFileNameWithoutExtension(originalName) + result.Extension;
            result.OutputStream.Position = 0;
            attachments.Add(new AttachmentProperties(outputName, result.OutputStream));
        }

        var ratio = oldTotal > 0 ? (double)newTotal / oldTotal : 1.0;
        var savingBytes = oldTotal - newTotal;

        if (applyThresholds && !ShouldCompress(ratio, savingBytes))
        {
            // oops! all useless
            results.ForEach(r => r.Result.OutputStream.Dispose());
            message.WithContent($"Compression skipped: {(ratio > _settings.Thresholds.NeverCompressRatio ? "insufficient savings" : "below minimum threshold")}");
            
            return new CompressMediaResult<T>
            {
                Message = message
            };
        }

        var compText = $@"({StringUtils.HumanReadableSize(oldTotal)} -> {StringUtils.HumanReadableSize(newTotal)} ({Math.Ceiling(ratio * 100)}%))";
        message.WithContent(compText);
        message.AddAttachments(attachments);

        return new CompressMediaResult<T>
        {
            Message = message,
            AttachedVideos = attachments
        };
    }

    private bool ShouldCompress(double ratio, long savingBytes)
    {
        if (ratio > _settings.Thresholds.NeverCompressRatio && ratio > _settings.Thresholds.AlwaysCompressRatio)
            return false;
        
        if (savingBytes < _settings.Thresholds.MinimumSavingBytes && ratio > _settings.Thresholds.AlwaysCompressRatio)
            return false;

        return true;
    }
}
