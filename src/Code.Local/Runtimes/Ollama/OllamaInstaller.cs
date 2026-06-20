using System;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Services;

namespace CodeLocal.Runtimes.Ollama;

/// <summary>
/// Installs the Ollama runtime on the local machine using Ollama's official install methods:
/// Windows runs the official PowerShell installer (<c>irm https://ollama.com/install.ps1 | iex</c>)
/// with a winget fallback; macOS and Linux run the official install script
/// (<c>curl -fsSL https://ollama.com/install.sh | sh</c>). Callers must obtain user consent
/// before invoking <see cref="InstallAsync"/>.
/// </summary>
public sealed class OllamaInstaller : IRuntimeInstaller
{
    private const string DownloadPage = "https://ollama.com/download";
    private const string WindowsInstallCommand = "irm https://ollama.com/install.ps1 | iex";
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
    /// Install Ollama on Windows by running the official PowerShell installer
    /// (<c>irm https://ollama.com/install.ps1 | iex</c>), falling back to winget. The official
    /// script downloads the latest signed installer, verifies its signature, and installs it
    /// silently per-user; winget's Ollama package often lags several releases behind, and an
    /// outdated Ollama breaks tool calling for newer models (ADR-13), so it is only a fallback.
    /// </summary>
    private static async Task<bool> InstallWindowsAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        if (await TryOfficialInstallScriptAsync(onLine, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        onLine("Falling back to winget...");

        return await TryWingetAsync(onLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Run Ollama's official Windows install script through Windows PowerShell. The script
    /// downloads the latest signed <c>OllamaSetup.exe</c>, verifies its Authenticode signature,
    /// and runs it silently per-user (no elevation). Returns false if PowerShell is missing or
    /// the script doesn't leave Ollama installed, so the caller can fall back to winget.
    /// </summary>
    private static async Task<bool> TryOfficialInstallScriptAsync(Action<string> onLine, CancellationToken cancellationToken)
    {
        string? powerShellPath = ProcessRunner.GetFullPathForExecutableOrNull("powershell");

        if (powerShellPath is null)
        {
            onLine("Windows PowerShell isn't available to run the official installer.");
            return false;
        }

        onLine("Installing the latest Ollama via the official installer (this can take a few minutes)...");
        int exitCode = await ProcessRunner.RunExecutableWithoutOutputCapturedAsync(
            powerShellPath,
            new[] { "-NoProfile", "-Command", WindowsInstallCommand },
            cancellationToken).ConfigureAwait(false);

        return exitCode == 0 && new OllamaService().IsInstalled;
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
