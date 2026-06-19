namespace CodeLocal.Services.Models;

/// <summary>
/// The GPU vendor detected on the machine.
/// </summary>
public enum GpuVendor
{
    /// <summary>
    /// No GPU detected, or the vendor could not be determined. This also covers vendors that we don't have special handling for (e.g. AMD, Intel, etc.).
    /// </summary>
    Unknown,

    /// <summary>
    /// An NVIDIA GPU (CUDA).
    /// </summary>
    Nvidia,

    /// <summary>
    /// Apple Silicon integrated GPU (Metal, unified memory).
    /// </summary>
    AppleSilicon,
}
