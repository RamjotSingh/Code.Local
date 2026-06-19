using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Runtimes;
using CodeLocal.Runtimes.Models;
using CodeLocal.Models;
using CodeLocal.Services;
using CodeLocal.Services.Models;
using CodeLocal.Copilot;
using Spectre.Console;

namespace CodeLocal.Commands.Status;

public static class StatusCommand
{
    /// <summary>
    /// Displays the hardware, runtime, and Copilot configuration status.
    /// </summary>
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // Hardware
        HardwareInfo hardware = await HardwareDetector.DetectAsync(cancellationToken).ConfigureAwait(false);
        HardwareReport.Render(hardware);

        // Resolve the saved (or environment-sourced) config first; it decides which runtime to show.
        CodeLocalConfig? config = ConfigStore.LoadOrNull();
        string configSource = "config file";

        if (config is null)
        {
            config = CopilotEnvironment.ReadFromEnvironmentOrNull();
            configSource = "environment";
        }

        // Runtime — the one this config uses (the default local runtime when nothing is configured).
        IModelRuntime runtime = RuntimeFactory.Create(config?.RuntimeKey);
        Table runtimeTable = new Table().Border(TableBorder.Rounded).AddColumn("Runtime").AddColumn("Value");
        runtimeTable.AddRow("Name", Markup.Escape(runtime.Name));

        if (runtime.ManagesLocalServer)
        {
            bool installed = runtime.IsInstalled;
            bool reachable = installed && await runtime.IsServerReachableAsync(cancellationToken).ConfigureAwait(false);
            runtimeTable.AddRow("Installed", installed ? "[green]yes[/]" : "[red]no[/]");
            runtimeTable.AddRow("Server", reachable ? "[green]running[/]" : "[red]not reachable[/]");
            AnsiConsole.Write(runtimeTable);

            if (reachable)
            {
                IReadOnlyList<InstalledModel> models = await runtime.ListModelsAsync(cancellationToken).ConfigureAwait(false);

                if (models.Count > 0)
                {
                    Table modelTable = new Table().Border(TableBorder.Rounded).AddColumn("Installed model").AddColumn("Size");

                    foreach (InstalledModel model in models)
                    {
                        modelTable.AddRow(Markup.Escape(model.Name), $"{model.SizeGb:0.0} GB");
                    }

                    AnsiConsole.Write(modelTable);
                }
            }
        }
        else
        {
            runtimeTable.AddRow("Type", "remote endpoint (not managed by Code.Local)");
            AnsiConsole.Write(runtimeTable);
        }

        // Copilot config
        Table configTable = new Table().Border(TableBorder.Rounded).AddColumn("Copilot config").AddColumn("Value");

        if (config is null)
        {
            configTable.AddRow("[yellow]not configured[/]", "run `codelocal init`");
        }
        else
        {
            configTable.AddRow("source", configSource);
            configTable.AddRow(CopilotEnvironment.EnvBaseUrl, Markup.Escape(config.BaseUrl));
            configTable.AddRow(CopilotEnvironment.EnvModel, Markup.Escape(config.Model));
            configTable.AddRow(CopilotEnvironment.EnvWireApi, Markup.Escape(config.WireApi));

            if (config.MaxPromptTokens is int maxPrompt)
            {
                configTable.AddRow(CopilotEnvironment.EnvMaxPromptTokens, maxPrompt.ToString(CultureInfo.InvariantCulture));
            }

            if (config.MaxOutputTokens is int maxOutput)
            {
                configTable.AddRow(CopilotEnvironment.EnvMaxOutputTokens, maxOutput.ToString(CultureInfo.InvariantCulture));
            }
        }

        AnsiConsole.Write(configTable);

        return 0;
    }
}
