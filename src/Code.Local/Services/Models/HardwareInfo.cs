namespace CodeLocal.Services.Models;

/// <summary>
/// Detected machine hardware relevant to running local models.
/// </summary>
public sealed class HardwareInfo
{
    /// <summary>
    /// Operating system name (e.g. "Windows", "macOS", "Linux").
    /// </summary>
    public string OsName { get; init; } = "";

    /// <summary>
    /// OS architecture (e.g. "X64", "Arm64").
    /// </summary>
    public string Architecture { get; init; } = "";

    /// <summary>
    /// Detected GPU name, or null if none was detected.
    /// </summary>
    public string? GpuName { get; init; }

    /// <summary>
    /// GPU VRAM in MB, or null if unknown (e.g. CPU-only).
    /// </summary>
    public int? VramMb { get; init; }

    /// <summary>
    /// Total system RAM in MB, or null if it couldn't be read.
    /// </summary>
    public long? TotalRamMb { get; init; }

    /// <summary>
    /// True when GPU and CPU share one memory pool (Apple Silicon / APU).
    /// </summary>
    public bool UnifiedMemory { get; init; }

    /// <summary>
    /// Detected GPU vendor.
    /// </summary>
    public GpuVendor Vendor { get; init; }

    /// <summary>
    /// GPU driver version string (e.g. "591.55"), or null if unknown.
    /// </summary>
    public string? GpuDriverVersion { get; init; }

    /// <summary>
    /// VRAM formatted for display (e.g. "6.0 GB"), or "unknown".
    /// </summary>
    public string VramDisplay => VramMb is int vram ? $"{vram / 1024.0:0.0} GB" : "unknown";

    /// <summary>
    /// System RAM formatted for display (e.g. "32.0 GB"), or "unknown".
    /// </summary>
    public string RamDisplay => TotalRamMb is long ram ? $"{ram / 1024.0:0.0} GB" : "unknown";
}
