using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Services;

namespace CodeLocal.Runtimes.Ollama;

/// <summary>
/// Installs the Ollama runtime on the local machine using Ollama's official install methods:
/// Windows downloads and silently runs the official <c>OllamaSetup.exe</c> (winget fallback);
/// macOS and Linux run the official install script
/// (<c>curl -fsSL https://ollama.com/install.sh | sh</c>). Callers must obtain user consent
/// before invoking <see cref="InstallAsync"/>.
/// </summary>
public sealed class OllamaInstaller : IRuntimeInstaller
{
    private const string DownloadPage = "https://ollama.com/download";
    private const string UnixInstallCommand = "curl -fsSL https://ollama.com/install.sh | sh";

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

        return await InstallUnixAsync(onLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Install Ollama on Windows by downloading and silently running the official
    /// <c>OllamaSetup.exe</c>, falling back to winget. We download the installer directly rather
    /// than piping Ollama's <c>install.ps1</c> to <c>iex</c> because that script's mandatory
    /// signature check (<c>Get-AuthenticodeSignature</c>) can fail to load
    /// <c>Microsoft.PowerShell.Security</c> in spawned PowerShell environments — and only after
    /// downloading the full installer. winget's Ollama package often lags several releases
    /// behind, and an outdated Ollama breaks tool calling for newer models (ADR-13), so it is
    /// only a fallback.
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
    /// Download the latest official Windows installer (<c>OllamaSetup.exe</c>) over HTTPS and
    /// run it silently (InnoSetup, per-user — no elevation needed). Returns false if curl is
    /// missing or the download/install doesn't leave Ollama installed, so the caller can fall
    /// back to winget.
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
    /// Install Ollama on macOS and Linux with Ollama's official install script
    /// (<c>curl -fsSL https://ollama.com/install.sh | sh</c>). The script detects the OS: on
    /// macOS it installs <c>Ollama.app</c> and links the <c>ollama</c> CLI into
    /// <c>/usr/local/bin</c>; on Linux it installs the binary under <c>/usr/local/bin</c> (or
    /// <c>/usr/bin</c>). Returns false if no shell is available or the script doesn't leave
    /// Ollama installed.
    /// </summary>
    private static async Task<bool> InstallUnixAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        string? shellPath = ProcessRunner.GetFullPathForExecutableOrNull("sh");

        if (shellPath is null)
        {
            onLine($"Unable to locate a shell. Install Ollama from {DownloadPage} instead.");
            return false;
        }

        onLine("Installing Ollama via the official install script...");
        int exitCode = await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(
            shellPath, new[] { "-c", UnixInstallCommand }, cancellationToken)
            .ConfigureAwait(false);

        return exitCode == 0 && new OllamaService().IsInstalled;
    }
}
