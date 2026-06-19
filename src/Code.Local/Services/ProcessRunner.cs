using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeLocal.Services;

/// <summary>
/// Helpers for locating and running external processes.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Resolve an executable name to a full path by scanning PATH (adds .exe etc. on Windows).
    /// </summary>
    /// <param name="executable">The executable name to resolve.</param>
    public static string? GetFullPathForExecutableOrNull(string executable)
    {
        string[] pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        string[] candidates = OperatingSystem.IsWindows()
            ? new[] { executable, executable + ".exe", executable + ".cmd", executable + ".bat" }
            : new[] { executable };

        foreach (string directory in pathDirectories)
        {
            foreach (string candidate in candidates)
            {
                try
                {
                    string fullPath = Path.Combine(directory.Trim('"'), candidate);

                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    // ignore malformed PATH entries
                }
            }
        }

        return null;
    }

    /// <summary>
    /// True when the path is a Windows command-script shim (.cmd or .bat), which can't be
    /// exec'd directly with UseShellExecute=false and must be run via the command interpreter.
    /// </summary>
    /// <param name="path">The executable path to test.</param>
    public static bool IsWindowsScript(string path)
    {
        return OperatingSystem.IsWindows()
            && (path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Run a process that inherits the terminal directly (no stdio redirection), so tools
    /// that draw their own progress UI (winget, curl, `ollama pull`) render correctly
    /// instead of having their carriage-return/ANSI output garbled by line capture.
    /// Returns the exit code.
    /// </summary>
    public static async Task<int> RunExecutableWithoutOutputCapturedAsync(
        string fileName, IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
        };

        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return process.ExitCode;
    }

    /// <summary>
    /// Run a process and capture stdout/stderr. Returns (exitCode, stdout, stderr).
    /// </summary>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunExecutableWithOutputCapturedAsync(
        string fileName, IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new Process { StartInfo = startInfo };
        process.Start();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }
}
