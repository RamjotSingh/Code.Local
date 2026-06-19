using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Commands.Copilot;
using CodeLocal.Commands.Init;
using CodeLocal.Commands.Package;
using CodeLocal.Commands.Speed;
using CodeLocal.Commands.Status;
using Spectre.Console;

internal class Program
{
    /// <summary>
    /// Entry point for the Code.Local CLI.
    /// </summary>
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "-h" || args[0] == "--help" || args[0] == "help")
        {
            PrintHelp();

            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] restArgs = args.Skip(1).ToArray();

        try
        {
            switch (command)
            {
                case "init":
                    return await InitCommand.RunAsync(InitOptions.Parse(ParseOptions(restArgs)), CancellationToken.None);
                case "status":
                    return await StatusCommand.RunAsync(CancellationToken.None);
                case "speed":
                    return await SpeedCommand.RunAsync(SpeedOptions.Parse(ParseOptions(restArgs)), CancellationToken.None);
                case "package":
                    return PackageCommand.Run(PackageOptions.Parse(ParseOptions(restArgs)));
                case "copilot":
                    return await CopilotCommand.RunAsync(restArgs, CancellationToken.None);
                case "version":
                case "--version":
                    return PrintVersion();
                default:
                    return Unknown(command);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Handles unknown command input.
    /// </summary>
    private static int Unknown(string command)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]unknown command:[/] {command}");
        PrintHelp();

        return 2;
    }

    /// <summary>
    /// Prints version information.
    /// </summary>
    private static int PrintVersion()
    {
        AnsiConsole.WriteLine("codelocal 0.1.0");

        return 0;
    }

    /// <summary>
    /// Prints help text for the CLI.
    /// </summary>
    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("[teal bold]codelocal[/] - set up local AI coding models for GitHub Copilot\n");
        AnsiConsole.MarkupLine("[bold]Usage:[/] codelocal <command> [[options]]\n");
        AnsiConsole.MarkupLine("[bold]Commands:[/]");
        AnsiConsole.MarkupLine("  init        Install/configure a local coding model and point Copilot at it");
        AnsiConsole.MarkupLine("  copilot     Launch GitHub Copilot in local mode (uses saved config)");
        AnsiConsole.MarkupLine("  status      Show hardware, runtime, and Copilot configuration");
        AnsiConsole.MarkupLine("  speed       Benchmark the configured model's generation speed (tokens/sec)");
        AnsiConsole.MarkupLine("  package     Generate a team installer that points another machine's Copilot at a shared endpoint");
        AnsiConsole.MarkupLine("  version     Print version\n");
        AnsiConsole.MarkupLine("[bold]init options:[/]");
        AnsiConsole.MarkupLine("  --non-interactive        Run without prompts");
        AnsiConsole.MarkupLine("  --runtime <ollama>       Model host runtime (default ollama)");
        AnsiConsole.MarkupLine("  --model <tag|id>         Model to use (e.g. qwen3.5:9b)");
        AnsiConsole.MarkupLine("  --ctx <tokens>           Override context window (default: auto-sized to VRAM; 32K floor, 256K max)");
        AnsiConsole.MarkupLine("  --vram <GB>              Override detected VRAM for model recommendation (unified-memory rigs)");
        AnsiConsole.MarkupLine("  --no-optimize            Skip runtime-specific performance tuning");
        AnsiConsole.MarkupLine("  --auto-install-dependencies  Install the runtime, the Copilot CLI, and prerequisites if missing (consent for non-interactive runs)");
        AnsiConsole.MarkupLine("  --persist                Also set user environment variables (always-on, every shell)");
        AnsiConsole.MarkupLine("  --skip-pull              Configure only; don't download or smoke-test");
        AnsiConsole.MarkupLine("  --skip-updating-path     Don't add the codelocal binary's folder to PATH (added by default)");
        AnsiConsole.MarkupLine("  --wire <completions|responses>  Copilot wire API (default completions; responses is experimental on Ollama)");
        AnsiConsole.MarkupLine("  --max-prompt-tokens <n>  Override prompt-token limit (default: context minus output budget)");
        AnsiConsole.MarkupLine("  --max-output-tokens <n>  Override output-token limit (default: a quarter of context, capped at 8192)");
        AnsiConsole.MarkupLine("  [grey]client mode (point at an existing endpoint instead of a local runtime):[/]");
        AnsiConsole.MarkupLine("  --endpoint <url>         Skip the local runtime; point Copilot at this endpoint (needs --model)");
        AnsiConsole.MarkupLine("  --api-key <key>          API key for --endpoint (default codelocal)\n");
        AnsiConsole.MarkupLine("[bold]copilot options:[/]");
        AnsiConsole.MarkupLine("  --offline                Also set COPILOT_OFFLINE=true for this launch");
        AnsiConsole.MarkupLine("  --auto-install-dependencies  Install the Copilot CLI if missing (consent for non-interactive runs)");
        AnsiConsole.MarkupLine("  -- <args>                Forward the remaining arguments to Copilot\n");
        AnsiConsole.MarkupLine("[bold]package options[/] (generate a team installer that points Copilot at a shared endpoint):");
        AnsiConsole.MarkupLine("  --endpoint <url>                (required) Shared OpenAI-compatible endpoint URL");
        AnsiConsole.MarkupLine("  --model <name>                  (required) Model name the endpoint serves");
        AnsiConsole.MarkupLine("  --wire <completions|responses>  Wire API (default completions)");
        AnsiConsole.MarkupLine("  --max-prompt-tokens <n>         (optional) Prompt-token limit baked into the installer");
        AnsiConsole.MarkupLine("  --max-output-tokens <n>         (optional) Output-token limit baked into the installer");
        AnsiConsole.MarkupLine("  --api-key <key>                 API key baked into the installer (default 'codelocal')");
        AnsiConsole.MarkupLine("  --os <all|windows|macos|linux>  Which installer(s) to emit (default all)");
        AnsiConsole.MarkupLine("  --out <dir>                     Output directory\n");
        AnsiConsole.MarkupLine("[bold]speed options[/] (warms the model up first, then times it):");
        AnsiConsole.MarkupLine("  --model <id>             Model to benchmark (default: the configured model)");
        AnsiConsole.MarkupLine("  --runs <n>               Measured runs to average (default 3)");
        AnsiConsole.MarkupLine("  --tokens <n>             Tokens to generate per run (default 256)");
    }

    /// <summary>
    /// Parses command-line options in --key value format.
    /// </summary>
    private static Dictionary<string, string> ParseOptions(string[] argv)
    {
        Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < argv.Length; index++)
        {
            if (!argv[index].StartsWith("--"))
            {
                continue;
            }

            string key = argv[index].Substring(2);

            if (index + 1 < argv.Length && !argv[index + 1].StartsWith("--"))
            {
                options[key] = argv[index + 1];
                index++;
            }
            else
            {
                options[key] = "true";
            }
        }

        return options;
    }
}
