namespace Dotto.Ffmpeg.Contracts;

public record CompressionOptions(
    CompressionMethod Method,
    int? Crf = null);
