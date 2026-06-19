using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodeLocal.Models;

namespace CodeLocal.Services;

/// <summary>
/// Emits self-contained installer scripts that point another machine's Copilot at a gateway.
/// </summary>
public static class InstallerGenerator
{
    /// <summary>
    /// Render the optional token-limit flags (quoted with <paramref name="quote"/>) when the
    /// config carries them, so the generated installer forwards them to `codelocal init`.
    /// </summary>
    private static string TokenArgs(CodeLocalConfig config, string quote)
    {
        StringBuilder builder = new StringBuilder();

        if (config.MaxPromptTokens is int promptTokens)
        {
            builder.Append($" --max-prompt-tokens {quote}{promptTokens.ToString(CultureInfo.InvariantCulture)}{quote}");
        }

        if (config.MaxOutputTokens is int outputTokens)
        {
            builder.Append($" --max-output-tokens {quote}{outputTokens.ToString(CultureInfo.InvariantCulture)}{quote}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Generate a PowerShell installer script for Windows.
    /// </summary>
    public static string PowerShell(CodeLocalConfig config)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("# Code.Local team installer (Windows).");
        builder.AppendLine("# Runs `codelocal init` in client mode: installs the Copilot CLI, writes the");
        builder.AppendLine("# provider config, and persists it so plain `copilot` uses the shared endpoint.");
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        builder.AppendLine("$bin = Join-Path $PSScriptRoot 'codelocal.exe'");
        builder.AppendLine("if (-not (Test-Path $bin)) {");
        builder.AppendLine("    Write-Error 'codelocal.exe was not found next to this script. Put the Windows codelocal binary here and re-run.'");
        builder.AppendLine("    exit 1");
        builder.AppendLine("}");
        builder.AppendLine("& $bin init --non-interactive --persist --auto-install-dependencies `");
        builder.AppendLine($"    --endpoint '{PowerShellEscape(config.BaseUrl)}' --api-key '{PowerShellEscape(config.ApiKey)}' --model '{PowerShellEscape(config.Model)}' --wire '{PowerShellEscape(config.WireApi)}'{TokenArgs(config, "'")}");

        return builder.ToString();
    }

    /// <summary>
    /// Generate a Bash installer script for macOS and Linux.
    /// </summary>
    public static string Bash(CodeLocalConfig config)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("#!/usr/bin/env bash\n");
        builder.Append("# Code.Local team installer (macOS / Linux).\n");
        builder.Append("# Runs `codelocal init` in client mode: installs the Copilot CLI, writes the\n");
        builder.Append("# provider config, and persists it so plain `copilot` uses the shared endpoint.\n");
        builder.Append("set -euo pipefail\n");
        builder.Append("DIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\n");
        builder.Append("BIN=\"$DIR/codelocal\"\n");
        builder.Append("if [ ! -x \"$BIN\" ]; then\n");
        builder.Append("  echo 'codelocal binary was not found next to this script. Put the macOS/Linux codelocal binary here and re-run.' >&2\n");
        builder.Append("  exit 1\n");
        builder.Append("fi\n");
        builder.Append("\"$BIN\" init --non-interactive --persist --auto-install-dependencies \\\n");
        builder.Append($"  --endpoint \"{ShellEscape(config.BaseUrl)}\" --api-key \"{ShellEscape(config.ApiKey)}\" --model \"{ShellEscape(config.Model)}\" --wire \"{ShellEscape(config.WireApi)}\"{TokenArgs(config, "\"")}\n");

        return builder.ToString();
    }

    /// <summary>
    /// Generate a README markdown for the installer bundle.
    /// </summary>
    public static string Readme(CodeLocalConfig config)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("# Code.Local team installer");
        builder.AppendLine();
        builder.AppendLine("Points this machine's GitHub Copilot at a shared endpoint by running");
        builder.AppendLine("`codelocal init` in client mode (no local model runtime required).");
        builder.AppendLine();
        builder.AppendLine($"- Endpoint: `{config.BaseUrl}`");
        builder.AppendLine($"- Model: `{config.Model}`");
        builder.AppendLine($"- Wire API: `{config.WireApi}`");

        if (config.MaxPromptTokens is int maxPromptTokens)
        {
            builder.AppendLine($"- Max prompt tokens: `{maxPromptTokens.ToString(CultureInfo.InvariantCulture)}`");
        }

        if (config.MaxOutputTokens is int maxOutputTokens)
        {
            builder.AppendLine($"- Max output tokens: `{maxOutputTokens.ToString(CultureInfo.InvariantCulture)}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Setup");
        builder.AppendLine();
        builder.AppendLine("Place the `codelocal` binary for your OS next to the install script");
        builder.AppendLine("(build them with `scripts/publish.ps1`, or grab them from a release), then:");
        builder.AppendLine();
        builder.AppendLine("### Windows");
        builder.AppendLine("```powershell");
        builder.AppendLine("powershell -ExecutionPolicy Bypass -File .\\install-copilot.ps1");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("### macOS / Linux");
        builder.AppendLine("```bash");
        builder.AppendLine("chmod +x ./codelocal && bash ./install-copilot.sh");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("The installer also installs the GitHub Copilot CLI if it's missing. Open a new");
        builder.AppendLine("terminal afterwards, then run `copilot` (or `codelocal copilot`).");

        return builder.ToString();
    }

    /// <summary>
    /// Write all installer files to the output directory.
    /// </summary>
    public static IReadOnlyList<string> WriteAll(CodeLocalConfig config, string outDir, string operatingSystem)
    {
        Directory.CreateDirectory(outDir);
        List<string> writtenFiles = new List<string>();

        void Write(string fileName, string fileContent)
        {
            string filePath = Path.Combine(outDir, fileName);
            File.WriteAllText(filePath, fileContent);
            writtenFiles.Add(filePath);
        }

        if (operatingSystem is "all" or "windows")
        {
            Write("install-copilot.ps1", PowerShell(config));
        }

        if (operatingSystem is "all" or "macos" or "linux")
        {
            Write("install-copilot.sh", Bash(config));
        }

        Write("README.md", Readme(config));

        return writtenFiles;
    }

    /// <summary>
    /// Escape a string for use in PowerShell single-quoted strings.
    /// </summary>
    private static string PowerShellEscape(string text)
    {
        return text.Replace("'", "''");
    }

    /// <summary>
    /// Escape a string for use in Bash double-quoted strings.
    /// </summary>
    private static string ShellEscape(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`");
    }
}
