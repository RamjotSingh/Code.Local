using System;
using System.Collections.Generic;
using System.IO;
using CodeLocal.Runtimes;
using CodeLocal.Services;

namespace CodeLocal.Copilot;

/// <summary>
/// Locates the GitHub Copilot CLI on the local machine and exposes an installer for it.
/// The Copilot CLI is the client Code.Local points at a local model; it is a dependency
/// rather than a model runtime, but it is installed through the same
/// <see cref="IRuntimeInstaller"/> abstraction.
/// </summary>
public sealed class CopilotCli
{
    /// <summary>
    /// Full path to the copilot executable/shim, or null if it can't be found.
    /// </summary>
    public string? ExecutablePath
    {
        get
        {
            string? executablePath = ProcessRunner.GetFullPathForExecutableOrNull("copilot");

            if (executablePath is not null)
            {
                return executablePath;
            }

            foreach (string candidatePath in KnownExecutableLocations())
            {
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// True if the Copilot CLI is present on this machine.
    /// </summary>
    public bool IsInstalled => ExecutablePath is not null;

    /// <summary>
    /// Installer for the Copilot CLI (npm-based).
    /// </summary>
    public IRuntimeInstaller Installer { get; } = new CopilotCliInstaller();

    /// <summary>
    /// Well-known npm global locations to probe when copilot isn't on PATH yet (e.g. a
    /// fresh install hasn't refreshed the current shell's PATH).
    /// </summary>
    private static List<string> KnownExecutableLocations()
    {
        List<string> locationPaths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            locationPaths.Add(Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "copilot.exe"));
            locationPaths.Add(Path.Combine(appData, "npm", "copilot.cmd"));
            locationPaths.Add(Path.Combine(appData, "npm", "copilot"));
        }
        else
        {
            string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            locationPaths.Add("/usr/local/bin/copilot");
            locationPaths.Add("/usr/bin/copilot");
            locationPaths.Add("/opt/homebrew/bin/copilot");
            locationPaths.Add(Path.Combine(homeDirectory, ".npm-global", "bin", "copilot"));
        }

        return locationPaths;
    }
}
