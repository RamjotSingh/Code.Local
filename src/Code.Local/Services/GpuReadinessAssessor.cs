using System;
using System.Globalization;
using CodeLocal.Services.Models;

namespace CodeLocal.Services;

/// <summary>
/// Turns detected hardware into a GPU-acceleration readiness note. Ollama bundles its
/// own CUDA runtime, so NVIDIA needs only a recent driver (no CUDA Toolkit); AMD needs
/// a ROCm/HIP-capable driver stack.
/// </summary>
public static class GpuReadinessAssessor
{
    private const int MinNvidiaDriverMajor = 531;

    /// <summary>
    /// Assess whether GPU acceleration is ready for the detected hardware.
    /// </summary>
    /// <param name="hardware">The detected hardware.</param>
    public static GpuReadiness Assess(HardwareInfo hardware)
    {
        if (hardware.Vendor == GpuVendor.Nvidia)
        {
            if (TryDriverMajor(hardware.GpuDriverVersion, out int driverMajor))
            {
                if (driverMajor >= MinNvidiaDriverMajor)
                {
                    return new GpuReadiness
                    {
                        Level = GpuReadinessLevel.Ready,
                        Message = $"GPU acceleration ready (NVIDIA driver {hardware.GpuDriverVersion}).",
                    };
                }

                return new GpuReadiness
                {
                    Level = GpuReadinessLevel.Warning,
                    Message = $"NVIDIA driver {hardware.GpuDriverVersion} is below {MinNvidiaDriverMajor}; update your GeForce/Studio driver for GPU acceleration. (Ollama bundles CUDA - no CUDA Toolkit needed.)",
                };
            }

            return new GpuReadiness
            {
                Level = GpuReadinessLevel.Info,
                Message = "NVIDIA GPU detected. Ensure driver 531+ for GPU acceleration (no CUDA Toolkit needed).",
            };
        }

        if (hardware.Vendor == GpuVendor.AppleSilicon)
        {
            return new GpuReadiness
            {
                Level = GpuReadinessLevel.Ready,
                Message = "Apple Silicon GPU (Metal) ready - no extra drivers needed.",
            };
        }

        return new GpuReadiness
        {
            Level = GpuReadinessLevel.Info,
            Message = "No NVIDIA GPU detected. For an AMD GPU install the ROCm/HIP-capable driver (see Ollama docs); otherwise Ollama runs on CPU.",
        };
    }

    /// <summary>
    /// Parse the major version number from an NVIDIA driver version string.
    /// </summary>
    /// <param name="version">Driver version (e.g. "591.55"), or null.</param>
    /// <param name="major">The parsed major version, or 0 on failure.</param>
    /// <returns>True if a major version was parsed.</returns>
    private static bool TryDriverMajor(string? version, out int major)
    {
        major = 0;

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        int dotIndex = version.IndexOf('.');
        string majorVersion = dotIndex >= 0 ? version.Substring(0, dotIndex) : version;

        return int.TryParse(majorVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
    }

    /// <summary>
    /// Spectre markup color for a readiness level.
    /// </summary>
    /// <param name="level">The readiness level.</param>
    public static string MarkupColor(GpuReadinessLevel level)
    {
        return level switch
        {
            GpuReadinessLevel.Ready => "green",
            GpuReadinessLevel.Warning => "yellow",
            _ => "grey",
        };
    }
}
