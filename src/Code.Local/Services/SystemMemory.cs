using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace CodeLocal.Services;

/// <summary>
/// Cross-platform detection of total physical system memory. Used both for display
/// and, on unified-memory machines, as the basis for GPU-usable memory.
/// </summary>
public static class SystemMemory
{
    /// <summary>
    /// Total physical RAM in megabytes, or null if it can't be determined.
    /// </summary>
    public static long? GetTotalRamMb()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return GetWindowsRamMb();
            }

            if (OperatingSystem.IsMacOS())
            {
                return GetMacRamMb();
            }

            if (OperatingSystem.IsLinux())
            {
                return GetLinuxRamMb();
            }
        }
        catch
        {
            // best effort only
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>
    /// Get total RAM in megabytes on Windows.
    /// </summary>
    private static long? GetWindowsRamMb()
    {
        MemoryStatusEx status = new MemoryStatusEx();
        status.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();

        if (!GlobalMemoryStatusEx(ref status))
        {
            return null;
        }

        return (long)(status.TotalPhys / (1024UL * 1024UL));
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int sysctlbyname(string name, ref long oldp, ref nuint oldlenp, IntPtr newp, nuint newlen);

    /// <summary>
    /// Get total RAM in megabytes on macOS.
    /// </summary>
    private static long? GetMacRamMb()
    {
        long memoryBytes = 0;
        nuint length = (nuint)sizeof(long);
        int resultCode = sysctlbyname("hw.memsize", ref memoryBytes, ref length, IntPtr.Zero, 0);

        if (resultCode != 0 || memoryBytes <= 0)
        {
            return null;
        }

        return memoryBytes / (1024 * 1024);
    }

    /// <summary>
    /// Get total RAM in megabytes on Linux.
    /// </summary>
    private static long? GetLinuxRamMb()
    {
        foreach (string meminfoLine in File.ReadLines("/proc/meminfo"))
        {
            if (!meminfoLine.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = meminfoLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length >= 2 && long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long kilobytes))
            {
                return kilobytes / 1024;
            }

            break;
        }

        return null;
    }
}
