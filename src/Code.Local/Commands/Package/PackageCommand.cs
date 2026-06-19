using System.Collections.Generic;
using System.IO;
using CodeLocal.Models;
using CodeLocal.Runtimes;
using CodeLocal.Services;
using Spectre.Console;

namespace CodeLocal.Commands.Package;

public static class PackageCommand
{
    /// <summary>
    /// Generates a team installer that points Copilot at a shared endpoint.
    /// </summary>
    public static int Run(PackageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            AnsiConsole.MarkupLine("[red]error:[/] --endpoint is required (the shared OpenAI-compatible endpoint to point Copilot at).");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            AnsiConsole.MarkupLine("[red]error:[/] --model is required (the model name the endpoint serves).");
            return 2;
        }

        if (options.OperatingSystem is not ("all" or "windows" or "macos" or "linux"))
        {
            AnsiConsole.MarkupLine("[red]error:[/] --os must be one of: all, windows, macos, linux.");
            return 2;
        }

        CodeLocalConfig config = new CodeLocalConfig
        {
            BaseUrl = options.Endpoint,
            Model = options.Model,
            ApiKey = options.ApiKey,
            WireApi = options.Wire,
            RuntimeKey = RemoteRuntime.RuntimeKey,
            MaxPromptTokens = options.MaxPromptTokens,
            MaxOutputTokens = options.MaxOutputTokens,
        };
        IReadOnlyList<string> writtenFiles = InstallerGenerator.WriteAll(config, options.OutputDirectory, options.OperatingSystem);

        AnsiConsole.MarkupLineInterpolated($"[green]Generated team installer in[/] {options.OutputDirectory}");

        foreach (string installerFile in writtenFiles)
        {
            AnsiConsole.MarkupLineInterpolated($"  - {Path.GetFileName(installerFile)}");
        }

        AnsiConsole.MarkupLine("Drop the `codelocal` binary for each target OS into that folder (see scripts/publish.ps1),");
        AnsiConsole.MarkupLine("then hand it over: the teammate runs the script for their OS and Copilot is wired to the endpoint.");

        return 0;
    }
}
