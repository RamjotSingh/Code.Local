using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Runtimes;
using CodeLocal.Services;

namespace CodeLocal.Copilot;

/// <summary>
/// Installs the GitHub Copilot CLI. On Windows it prefers winget (`GitHub.Copilot`,
/// which doesn't require Node.js) and falls back to npm; elsewhere it uses npm
/// (`npm install -g @github/copilot`, Node.js 18+ required).
/// </summary>
public sealed class CopilotCliInstaller : IRuntimeInstaller
{
    private const string NpmPackage = "@github/copilot";
    private const string WingetId = "GitHub.Copilot";

    public string ManualInstallHint
        => OperatingSystem.IsWindows()
            ? $"Install with `winget install --id {WingetId} -e` or `npm install -g {NpmPackage}` (Node.js 18+)."
            : $"Install Node.js 18+ from https://nodejs.org, then run `npm install -g {NpmPackage}`.";

    /// <summary>
    /// Installs the GitHub Copilot CLI (npm or winget).
    /// </summary>
    public async Task<bool> InstallAsync(Action<string> onLine, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows() && await TryWingetAsync(onLine, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await TryNpmAsync(onLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to install the Copilot CLI via winget.
    /// </summary>
    private static async Task<bool> TryWingetAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        string? wingetPath = ProcessRunner.GetFullPathForExecutableOrNull("winget");

        if (wingetPath is null)
        {
            return false;
        }

        onLine($"Installing the GitHub Copilot CLI via winget ({WingetId})...");
        int exitCode = await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(
            wingetPath,
            new[]
            {
                "install", "--id", WingetId, "-e", "--silent",
                "--disable-interactivity",
                "--accept-source-agreements", "--accept-package-agreements",
            },
            cancellationToken).ConfigureAwait(false);

        if (exitCode == 0 && new CopilotCli().IsInstalled)
        {
            return true;
        }

        onLine("winget install did not complete; falling back to npm.");
        return false;
    }

    /// <summary>
    /// Attempts to install the Copilot CLI via npm.
    /// </summary>
    private static async Task<bool> TryNpmAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        string? npmPath = ProcessRunner.GetFullPathForExecutableOrNull("npm");

        if (npmPath is null)
        {
            onLine("npm was not found. Install Node.js 18+ first (https://nodejs.org), then re-run.");
            return false;
        }

        onLine($"Installing {NpmPackage} via npm (this can take a minute)...");
        string[] npmArguments = { "install", "-g", NpmPackage };
        int exitCode;
        if (ProcessRunner.IsWindowsScript(npmPath))
        {
            // npm resolves to npm.cmd on Windows, which can't be exec'd directly with
            // UseShellExecute=false, so run it through the command interpreter.
            string commandInterpreter = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            List<string> commandArguments = new List<string> { "/c", npmPath };
            commandArguments.AddRange(npmArguments);
            exitCode = await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(commandInterpreter, commandArguments, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            exitCode = await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(npmPath, npmArguments, cancellationToken).ConfigureAwait(false);
        }

        if (exitCode != 0)
        {
            onLine($"npm install exited with code {exitCode}.");
            return false;
        }

        return new CopilotCli().IsInstalled;
    }
}
