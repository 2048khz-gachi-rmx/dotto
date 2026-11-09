using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

namespace Dotto.Ai.Settings;

public class SandboxOptions
{
    public bool Enabled { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string ImageTag { get; set; } = "dotto-sandbox:latest";

    /// <summary>
    /// Docker network to attach sandbox containers to. When set, the bot reaches
    /// the sandbox via the container's name on this network (production: bot runs
    /// in a container). When null/empty, the sandbox publishes port 8080 to a
    /// random loopback port and the bot connects via 127.0.0.1 (dev: bot runs on
    /// the host).
    /// </summary>
    public string? NetworkName { get; set; }

    /// <summary>
    /// Base directory for per-caller persistent storage (<c>/storage</c> in the
    /// sandbox). The bot only creates the directory and bind-mounts it; it never
    /// reads or writes its contents.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string StorageBasePath { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Path.GetTempPath(), "dotto-sandbox-storage")
        : "/var/lib/dotto-sandbox";

    /// <summary>
    /// Per-caller persistent storage quota in megabytes. Enforced by the sandbox
    /// server before writes and by the execution watchdog.
    /// </summary>
    [Range(16, 10240)]
    public long StorageQuotaMb { get; set; } = 200;

    /// <summary>
    /// Size of the ephemeral <c>/work</c> tmpfs in megabytes. Kernels cannot
    /// quota tmpfs per directory, but a sized tmpfs is a hard limit. tmpfs pages
    /// count against the container memory cgroup, so <see cref="MemoryMb"/> must
    /// exceed this value.
    /// </summary>
    [Range(64, 16384)]
    public long WorkTmpfsMb { get; set; } = 1024;

    [Range(typeof(TimeSpan), "00:00:05", "00:05:00")]
    public TimeSpan ContainerStartTimeout { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:02:00", "04:00:00")]
    public TimeSpan WallClockTimeout { get; set; } = TimeSpan.FromMinutes(15);

    [Range(0, double.MaxValue)]
    public double CpuCount { get; set; } = 1.0;

    [Range(256, 32768)]
    public long MemoryMb { get; set; } = 1536;

    public bool UseGVisorRuntime { get; set; } = false;
}
