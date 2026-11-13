using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

namespace Dotto.Ai.Settings;

public class SandboxOptions
{
    public bool Enabled { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string ImageTag { get; set; } = "dotto-sandbox:latest";

    /// <summary>
    /// Network to attach sandboxes to; unset = publish 8080 on loopback (dev).
    /// </summary>
    public string? NetworkName { get; set; }

    /// <summary>
    /// Host directory per caller, bind-mounted as /storage.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string StorageBasePath { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Path.GetTempPath(), "dotto-sandbox-storage")
        : "/var/lib/dotto-sandbox";

    /// <summary>Host directory for per-session sockets; bot and daemon must resolve it identically.</summary>
    [Required(AllowEmptyStrings = false)]
    public string IpcBasePath { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Path.GetTempPath(), "dotto-sandbox-ipc")
        : "/var/lib/dotto-sandbox-ipc";

    /// <summary>
    /// Per-caller /storage quota in MB (enforced inside the sandbox).
    /// </summary>
    [Range(16, 10240)]
    public long StorageQuotaMb { get; set; } = 200;

    /// <summary>
    /// /work tmpfs size; its pages count against MemoryMb, which must be larger.
    /// </summary>
    [Range(64, 16384)]
    public long WorkTmpfsMb { get; set; } = 1024;

    [Range(typeof(TimeSpan), "00:00:05", "00:05:00")]
    public TimeSpan ContainerStartTimeout { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:02:00", "04:00:00")]
    public TimeSpan WallClockTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>CPU quota; gVisor guests see <c>max(2, ceil(quota))</c> CPUs, runc sees all host CPUs.</summary>
    [Range(0, double.MaxValue)]
    public double CpuCount { get; set; } = 1.0;

    [Range(256, 32768)]
    public long MemoryMb { get; set; } = 1536;

    /// <summary>
    /// Run on gVisor. Requires a registered <c>runsc</c> with <c>--host-uds=create</c>;
    /// there is no automatic runc fallback.
    /// </summary>
    public bool UseGVisorRuntime { get; set; } = true;

    /// <summary>
    /// Sandbox resolvers; empty falls back to 1.1.1.1/8.8.8.8. Private resolvers are
    /// unreachable through the egress filter.
    /// </summary>
    public List<string> DnsServers { get; set; } = [];
}
