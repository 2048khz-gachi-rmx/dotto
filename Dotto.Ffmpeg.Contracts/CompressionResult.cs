namespace Dotto.Ffmpeg.Contracts;

 public record CompressionResult(
    Stream OutputStream,
    long OriginalSize,
    long CompressedSize,
    bool Success,
    string? ErrorMessage,
    string Extension);
