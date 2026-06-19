using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Services;

namespace CodeLocal.Runtimes.Ollama;

/// <summary>
/// Installs the Ollama runtime on the local machine. Windows prefers the official installer
/// (winget fallback) so it gets the latest release; macOS uses Homebrew; Linux uses the
/// official install script. Callers must obtain user consent before invoking <see cref="InstallAsync"/>.
/// </summary>
public sealed class OllamaInstaller : IRuntimeInstaller
{
    private const string DownloadPage = "https://ollama.com/download";

    public string ManualInstallHint
        => $"Install Ollama from {DownloadPage}, then re-run `codelocal init` from a new terminal.";

    /// <summary>
    /// Install Ollama using the platform's native mechanism.
    /// </summary>
    public async Task<bool> InstallAsync(Action<string> onLine, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return await InstallWindowsAsync(onLine, cancellationToken).ConfigureAwait(false);
        }

        if (OperatingSystem.IsMacOS())
        {
            return await InstallMacOsAsync(onLine, cancellationToken).ConfigureAwait(false);
        }

        return await InstallLinuxAsync(onLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Install Ollama on Windows, preferring the official installer (always the latest
    /// release) and falling back to winget. winget's Ollama package often lags several
    /// releases behind, and an outdated Ollama breaks tool calling for newer models (ADR-13).
    /// </summary>
    private static async Task<bool> InstallWindowsAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        if (await TryOfficialInstallerAsync(onLine, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        onLine("Falling back to winget...");

        return await TryWingetAsync(onLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Download and silently run the official Windows installer (InnoSetup, per-user — no
    /// elevation needed). Returns false if curl is missing or the download/install doesn't
    /// leave Ollama installed, so the caller can fall back to winget.
    /// </summary>
    private static async Task<bool> TryOfficialInstallerAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        string? curlPath = ProcessRunner.GetFullPathForExecutableOrNull("curl");

        if (curlPath is null)
        {
            onLine("curl isn't available to download the official installer.");
            return false;
        }

        string installerUrl = $"{DownloadPage}/OllamaSetup.exe";
        string installerPath = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");

        onLine("Downloading the latest Ollama installer (this can take a few minutes)...");
        int downloadExitCode = await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(
            curlPath, new[] { "-fSL", "-o", installerPath, installerUrl }, cancellationToken).ConfigureAwait(false);

        if (downloadExitCode != 0)
        {
            onLine("Download failed.");
            return false;
        }

        onLine("Running the Ollama installer (silent)...");
        await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(
            installerPath, new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" }, cancellationToken).ConfigureAwait(false);

        try
        {
            File.Delete(installerPath);
        }
        catch
        {
            /* best effort */
        }

        return new OllamaService().IsInstalled;
    }

    /// <summary>
    /// Install Ollama via winget. Returns false if winget is missing or the install doesn't
    /// leave Ollama installed.
    /// </summary>
    private static async Task<bool> TryWingetAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        string? wingetPath = ProcessRunner.GetFullPathForExecutableOrNull("winget");

        if (wingetPath is null)
        {
            onLine("winget isn't available.");
            return false;
        }

        onLine("Installing Ollama via winget...");
        int exitCode = await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(
            wingetPath,
            new[]
            {
                "install", "--id", "Ollama.Ollama", "-e", "--silent",
                "--disable-interactivity",
                "--accept-source-agreements", "--accept-package-agreements",
            },
            cancellationToken).ConfigureAwait(false);

        return exitCode == 0 && new OllamaService().IsInstalled;
    }

    /// <summary>
    /// Install Ollama on macOS using Homebrew.
    /// </summary>
    private static async Task<bool> InstallMacOsAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        string? brewPath = ProcessRunner.GetFullPathForExecutableOrNull("brew");

        if (brewPath is null)
        {
            onLine($"Homebrew not found. Install Ollama from {DownloadPage} instead.");
            return false;
        }

        onLine("Installing Ollama via Homebrew...");
        int exitCode = await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(
            brewPath, new[] { "install", "ollama" }, cancellationToken).ConfigureAwait(false);

        return exitCode == 0 && new OllamaService().IsInstalled;
    }

    /// <summary>
    /// Install Ollama on Linux using the official install script.
    /// </summary>
    private static async Task<bool> InstallLinuxAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        string? shellPath = ProcessRunner.GetFullPathForExecutableOrNull("sh");

        if (shellPath is null)
        {
            onLine($"Unable to locate a shell. Install Ollama from {DownloadPage} instead.");
            return false;
        }

        onLine("Installing Ollama via the official install script...");
        int exitCode = await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(
            shellPath, new[] { "-c", "curl -fsSL https://ollama.com/install.sh | sh" }, cancellationToken)
            .ConfigureAwait(false);

        return exitCode == 0 && new OllamaService().IsInstalled;
    }
}
