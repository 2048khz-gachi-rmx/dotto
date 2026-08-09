using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

namespace Dotto.Ai.Settings;

public class SandboxOptions
{
    public bool Enabled { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string ImageTag { get; set; } = "dotto-sandbox:latest";

    public string HostBasePath { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Path.GetTempPath(), "dotto-sandbox")
        : "/tmp/dotto-sandbox";

    [Range(typeof(TimeSpan), "00:00:05", "00:05:00")]
    public TimeSpan ContainerStartTimeout { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:02:00", "04:00:00")]
    public TimeSpan WallClockTimeout { get; set; } = TimeSpan.FromMinutes(15);

    [Range(0, double.MaxValue)]
    public double CpuCount { get; set; } = 1.0;

    [Range(64, 32768)]
    public long MemoryMb { get; set; } = 512;

    public bool UseGVisorRuntime { get; set; } = false;
}