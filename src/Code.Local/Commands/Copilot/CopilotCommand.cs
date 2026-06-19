using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Copilot;
using CodeLocal.Models;
using CodeLocal.Runtimes;
using CodeLocal.Services;
using Spectre.Console;

namespace CodeLocal.Commands.Copilot;

/// <summary>
/// Launches the GitHub Copilot CLI in local mode by injecting the saved provider
/// configuration into the child process environment only. Nothing global is mutated:
/// a plain `copilot` in any other shell still runs in normal (cloud) mode.
/// </summary>
public static class CopilotCommand
{
    /// <summary>
    /// Launches the Copilot CLI with the saved local configuration in a child process.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        CodeLocalConfig? config = ConfigStore.LoadOrNull();

        if (config is null)
        {
            AnsiConsole.MarkupLine("[red]error:[/] no Code.Local configuration found. Run `codelocal init` first.");
            return 3;
        }

        bool offline = false;
        bool autoInstall = false;
        List<string> forwardedArgumentsToCopilot = new List<string>();
        bool afterSeparator = false;

        foreach (string argument in args)
        {
            // See the comment below for why we forward arguments after the `--` separator to the Copilot CLI instead of consuming them here.
            if (afterSeparator)
            {
                forwardedArgumentsToCopilot.Add(argument);
                continue;
            }

            if (argument == "--offline")
            {
                offline = true;
                continue;
            }

            if (argument == "--auto-install-dependencies")
            {
                autoInstall = true;
                continue;
            }

            // Essentially any options after the `--` separator are forwarded to the Copilot CLI, so that users can pass through flags like `--help` or `--version` or anything
            // that might conflict with our options. So for example we have --auto-install-dependencies but if copilot had the same option and we (or user) would want to forward it to 
            // the child process instead of consuming it here user can do -- --auto-install-dependencies and then we wont process it but copilot will.
            if (argument == "--")
            {
                afterSeparator = true;
                continue;
            }

            forwardedArgumentsToCopilot.Add(argument);
        }

        CopilotCli cli = new CopilotCli();

        // Check if the Copilot CLI is installed, and if not, attempt to install it (if autoInstall is true or if the user consents interactively).
        if (!cli.IsInstalled)
        {
            bool interactive = !Console.IsInputRedirected;
            bool installed = await DependencyInstallationManager.EnsureInstalledAsync(
                "GitHub Copilot CLI", cli.IsInstalled, cli.Installer, interactive, autoInstall, cancellationToken).ConfigureAwait(false);

            // Check both installation returned true AND that CLI is installed, because the installation may have failed or the user may have declined to install it. Just double checks.
            if (!installed || !cli.IsInstalled)
            {
                AnsiConsole.MarkupLine("[red]error:[/] the GitHub Copilot CLI is required to launch local mode.");
                return 4;
            }
        }

        string copilotPath = cli.ExecutablePath!;

        // If the saved config targets a local runtime, make sure its server is running
        // before launching — otherwise Copilot would fire requests at a dead endpoint.
        await EnsureModelRuntimeRunningAsync(config, cancellationToken).ConfigureAwait(false);

        ProcessStartInfo startInfo = BuildStartInfo(copilotPath, forwardedArgumentsToCopilot);

        foreach (KeyValuePair<string, string> pair in CopilotEnvironment.BuildEnvironmentVariables(config))
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        // Run copilot fully offline no online communication, great for privacy focused use!
        if (offline)
        {
            startInfo.Environment["COPILOT_OFFLINE"] = "true";
        }

        AnsiConsole.MarkupLineInterpolated(
            $"[grey]Launching Copilot via Code.Local -> {config.BaseUrl} ({config.Model}){(offline ? ", offline" : "")}[/]");

        using Process process = new Process { StartInfo = startInfo };

        // Keep the launcher alive on Ctrl+C so the child Copilot handles the signal itself.
        ConsoleCancelEventHandler handler = (sender, e) => { e.Cancel = true; };
        Console.CancelKeyPress += handler;

        try
        {
            process.Start();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    /// <summary>
    /// When the saved config points at a local runtime, ensure that runtime's server is
    /// running (starting it if needed). Best effort: warns but still launches Copilot. A
    /// remote-endpoint config resolves to a runtime whose server management is a no-op, so
    /// nothing happens in that case.
    /// </summary>
    private static async Task EnsureModelRuntimeRunningAsync(CodeLocalConfig config, CancellationToken cancellationToken)
    {
        IModelRuntime runtime;
        try
        {
            runtime = RuntimeFactory.Create(config.RuntimeKey);
        }
        catch
        {
            return;
        }

        if (!runtime.IsInstalled)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]note:[/] {runtime.Name} isn't installed; the local model may be unavailable. Run `codelocal init`.");
            return;
        }

        if (!await runtime.EnsureServerRunningAsync(AnsiConsole.WriteLine, cancellationToken).ConfigureAwait(false))
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]note:[/] couldn't reach the {runtime.Name} server; the local model may be unavailable.");
        }
    }

    /// <summary>
    /// Builds a ProcessStartInfo to launch the Copilot CLI with the given arguments.
    /// </summary>
    private static ProcessStartInfo BuildStartInfo(string copilotPath, List<string> forwardedArguments)
    {
        // Inherit the terminal: do NOT redirect stdio so Copilot's interactive UI works.
        bool isScript = ProcessRunner.IsWindowsScript(copilotPath);

        ProcessStartInfo startInfo;
        if (isScript)
        {
            // .cmd/.bat shims (e.g. from an npm global install) cannot be exec'd directly
            // with UseShellExecute=false, so run them through the command interpreter.
            string comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo = new ProcessStartInfo(comspec) { UseShellExecute = false };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(copilotPath);
        }
        else
        {
            startInfo = new ProcessStartInfo(copilotPath) { UseShellExecute = false };
        }

        foreach (string argument in forwardedArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
