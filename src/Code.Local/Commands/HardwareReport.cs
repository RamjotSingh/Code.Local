using CodeLocal.Services.Models;
using CodeLocal.Services;
using Spectre.Console;

namespace CodeLocal.Commands;

/// <summary>
/// Renders the detected hardware as a table plus a GPU-acceleration readiness line. Shared
/// by `init` and `status` so both show the same hardware summary.
/// </summary>
public static class HardwareReport
{
    /// <summary>
    /// Writes the hardware table (OS, GPU, VRAM, system RAM) followed by a colored
    /// GPU-readiness note.
    /// </summary>
    /// <param name="hardware">The detected hardware to display.</param>
    public static void Render(HardwareInfo hardware)
    {
        Table table = new Table().Border(TableBorder.Rounded).AddColumn("Hardware").AddColumn("Value");
        table.AddRow("OS", $"{hardware.OsName} ({hardware.Architecture})");
        table.AddRow("GPU", hardware.GpuName ?? "[grey]not detected[/]");
        table.AddRow("VRAM", hardware.UnifiedMemory ? $"{hardware.VramDisplay} (unified, usable)" : hardware.VramDisplay);
        table.AddRow("System RAM", hardware.RamDisplay);
        AnsiConsole.Write(table);

        GpuReadiness readiness = GpuReadinessAssessor.Assess(hardware);
        AnsiConsole.MarkupLine($"[{GpuReadinessAssessor.MarkupColor(readiness.Level)}]{Markup.Escape(readiness.Message)}[/]");
    }
}
