using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Services.Models;

namespace CodeLocal.Services;

public static class HardwareDetector
{
    // macOS lets the GPU use a large fraction of unified memory by default (~75%).
    // Use a conservative fraction so we never recommend a model that won't fit.
    private const double UnifiedUsableFraction = 0.75;

    /// <summary>
    /// Detect hardware information including GPU, VRAM, RAM, and architecture.
    /// </summary>
    public static async Task<HardwareInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        string os = OperatingSystem.IsWindows() ? "Windows"
               : OperatingSystem.IsMacOS() ? "macOS"
               : OperatingSystem.IsLinux() ? "Linux"
               : "Unknown";
        string arch = RuntimeInformation.OSArchitecture.ToString();

        // 1. NVIDIA via nvidia-smi (covers discrete RTX and unified GB10 parts like RTX/DGX Spark).
        (string? gpu, int? vram, string? driver) = await TryNvidiaAsync(cancellationToken).ConfigureAwait(false);

        long? totalRamMb = SystemMemory.GetTotalRamMb();
        bool unified = false;
        GpuVendor vendor = gpu is not null ? GpuVendor.Nvidia : GpuVendor.Unknown;

        // 2. Apple Silicon: no nvidia-smi, but the GPU shares unified memory with the CPU.
        if (vram is null
            && OperatingSystem.IsMacOS()
            && RuntimeInformation.OSArchitecture == Architecture.Arm64
            && totalRamMb is long ramMb)
        {
            gpu = "Apple Silicon (unified memory)";
            vram = (int)Math.Min(int.MaxValue, (long)(ramMb * UnifiedUsableFraction));
            unified = true;
            vendor = GpuVendor.AppleSilicon;
        }

        return new HardwareInfo
        {
            OsName = os,
            Architecture = arch,
            GpuName = gpu,
            VramMb = vram,
            TotalRamMb = totalRamMb,
            UnifiedMemory = unified,
            Vendor = vendor,
            GpuDriverVersion = driver,
        };
    }

    /// <summary>
    /// Try to detect NVIDIA GPU information via nvidia-smi.
    /// </summary>
    private static async Task<(string? Gpu, int? VramMb, string? Driver)> TryNvidiaAsync(CancellationToken cancellationToken)
    {
        string? nvidiaSmiPath = ProcessRunner.GetFullPathForExecutableOrNull("nvidia-smi");

        if (nvidiaSmiPath is null)
        {
            return (null, null, null);
        }

        try
        {
            (int exitCode, string stdout, _) = await ProcessRunner.RunExecutableWithOutputCapturedAsync(
                nvidiaSmiPath,
                new[] { "--query-gpu=name,memory.total,driver_version", "--format=csv,noheader,nounits" },
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return (null, null, null);
            }

            string firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            string[] csvFields = firstLine.Split(',', StringSplitOptions.TrimEntries);

            if (csvFields.Length < 2)
            {
                return (null, null, null);
            }

            int? vram = int.TryParse(csvFields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int vramMb)
                ? vramMb
                : (int?)null;
            string? driver = csvFields.Length >= 3 && csvFields[2].Length > 0 ? csvFields[2] : null;

            return (csvFields[0], vram, driver);
        }
        catch
        {
            return (null, null, null);
        }
    }
}
