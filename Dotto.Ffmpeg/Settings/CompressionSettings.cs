using Dotto.Ffmpeg.Contracts;

namespace Dotto.Ffmpeg.Settings;

public class CompressionSettings
{
    public ThresholdsSettings Thresholds { get; set; } = new();
    public Dictionary<CompressionMethod, StrategySettings> Strategies { get; set; } = new();
    public CompressionMethod DefaultStrategy { get; set; } = CompressionMethod.Vp9;
}

public class ThresholdsSettings
{
    public double NeverCompressRatio { get; set; } = 0.90;
    public double AlwaysCompressRatio { get; set; } = 0.50;
    public long MinimumSavingBytes { get; set; } = 1048576;
}

public class StrategySettings
{
    public int Crf { get; set; } = 35;
    public int AudioBitrateKbps { get; set; } = 80;
}
