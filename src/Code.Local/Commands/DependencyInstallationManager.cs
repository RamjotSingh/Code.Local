using System;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Runtimes;
using Spectre.Console;

namespace CodeLocal.Commands;

/// <summary>
/// Centralizes the consent-then-install flow for a missing dependency (a model runtime or
/// the Copilot CLI). Prompts interactively, or uses caller-supplied auto-consent for
/// non-interactive runs, and shows the manual install hint when declined or unavailable.
/// </summary>
public static class DependencyInstallationManager
{
    /// <summary>
    /// Prompts for consent and installs a missing dependency if approved.
    /// </summary>
    public static async Task<bool> EnsureInstalledAsync(
        string name,
        bool isInstalled,
        IRuntimeInstaller? installer,
        bool interactive,
        bool autoConsent,
        CancellationToken cancellationToken)
    {
        if (isInstalled)
        {
            return true;
        }

        if (installer is null)
        {
            AnsiConsole.MarkupLineInterpolated($"\n[yellow]{name} is not installed.[/]");
            return false;
        }

        bool consent;
        if (interactive)
        {
            AnsiConsole.MarkupLineInterpolated($"\n[yellow]{name} is not installed.[/]");
            consent = AnsiConsole.Confirm($"Install {name} now?");
        }
        else
        {
            consent = autoConsent;
        }

        if (!consent)
        {
            AnsiConsole.MarkupLineInterpolated($"{installer.ManualInstallHint}");
            return false;
        }

        AnsiConsole.MarkupLineInterpolated($"\nInstalling {name}...");
        bool installed = await installer.InstallAsync(AnsiConsole.WriteLine, cancellationToken).ConfigureAwait(false);

        if (!installed)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]error:[/] automatic installation of {name} did not complete. {installer.ManualInstallHint}");
        }

        return installed;
    }
}
