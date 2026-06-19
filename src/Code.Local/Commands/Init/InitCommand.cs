using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Runtimes;
using CodeLocal.Copilot;
using CodeLocal.Models;
using CodeLocal.Services;
using CodeLocal.Services.Models;
using Spectre.Console;

namespace CodeLocal.Commands.Init;

public static class InitCommand
{
    /// <summary>
    /// Configures a local coding model and points Copilot at it, or points at an existing endpoint.
    /// </summary>
    public static async Task<int> RunAsync(InitOptions options, CancellationToken cancellationToken)
    {
        // Client mode: when an endpoint is supplied, skip the local runtime entirely and
        // just point Copilot at it. This is the payload the team installer runs.
        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return await RunGatewayAsync(options, cancellationToken).ConfigureAwait(false);
        }

        return await RunLocalAsync(options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Local mode: provision the chosen coding model on a local runtime (install and start
    /// the runtime, pull and size the model), point Copilot at it, and run a smoke test.
    /// </summary>
    private static async Task<int> RunLocalAsync(InitOptions options, CancellationToken cancellationToken)
    {
        IModelRuntime runtime = RuntimeFactory.Create(options.RuntimeKey);

        PrintBanner(options.Interactive);

        // 1. Hardware
        HardwareInfo hardware = await HardwareDetector.DetectAsync(cancellationToken).ConfigureAwait(false);
        HardwareReport.Render(hardware);

        int? effectiveVram = options.VramOverrideMb ?? hardware.VramMb;

        if (options.VramOverrideMb is int overrideMb)
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Using --vram override: {overrideMb / 1024.0:0.0} GB for model recommendation.[/]");
        }
        else if (hardware.VramMb is null)
        {
            AnsiConsole.MarkupLine("[grey]No GPU VRAM detected. On a unified-memory APU (e.g. Strix Halo) pass --vram <GB> to pick a larger model.[/]");
        }

        // 2. Make sure the runtime is installed and its server is actually running.
        int readyResult = await EnsureRuntimeReadyAsync(runtime, options.Interactive, options.InstallDependencies, cancellationToken).ConfigureAwait(false);

        if (readyResult != 0)
        {
            return readyResult;
        }

        // 2b. Ensure the Copilot CLI itself is available so `codelocal copilot` works.
        await EnsureCopilotInstalledAsync(options.Interactive, options.InstallDependencies, cancellationToken).ConfigureAwait(false);

        // 3. Choose model (only those this runtime can host)
        List<CodingModel> supportedModels = new List<CodingModel>();

        foreach (CodingModel candidate in ModelCatalog.All)
        {
            if (runtime.Supports(candidate))
            {
                supportedModels.Add(candidate);
            }
        }

        if (supportedModels.Count == 0)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] no catalog models are available for {runtime.Name}.");
            return 3;
        }

        CodingModel recommended = ModelCatalog.Recommend(effectiveVram, supportedModels);
        CodingModel model;
        if (options.Interactive)
        {
            model = AnsiConsole.Prompt(
                new SelectionPrompt<CodingModel>()
                    .Title("\nSelect a [green]coding model[/]:")
                    .PageSize(12)
                    .UseConverter(candidate => FormatModelChoice(candidate, recommended, effectiveVram))
                    .AddChoices(supportedModels));
        }
        else
        {
            model = (options.Model is { Length: > 0 } requested ? ModelCatalog.FindByTagOrId(requested) : null) ?? recommended;
        }

        string modelReference = runtime.ModelRef(model) ?? model.DisplayName;
        AnsiConsole.MarkupLineInterpolated($"Using [green]{model.DisplayName}[/] ([blue]{modelReference}[/]).");

        if (effectiveVram is int vm && vm < model.MinVramMb)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]warning:[/] detected VRAM ({hardware.VramDisplay}) is below the ~{model.MinVramMb / 1024.0:0.0} GB suggested for this model; it may run slowly or offload to RAM.");
        }

        // 4. Context window - sized to fit the GPU so we don't silently offload to CPU.
        //    KV cache grows linearly with context, so a flat 32K can overflow small GPUs.
        KvCachePrecision kvPrecision = options.Optimize ? KvCachePrecision.Q8 : KvCachePrecision.Fp16;
        (int? usableVramMb, long usableRamMb) = ComputeBudget(hardware, effectiveVram);

        int numCtx;
        if (options.ContextWindow is int requestedContext)
        {
            numCtx = Math.Clamp(requestedContext, MemoryEstimator.MinContext, MemoryEstimator.MaxContext);

            if (numCtx != requestedContext)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[grey]Clamped --ctx {requestedContext} to {numCtx} (allowed {MemoryEstimator.MinContext}-{MemoryEstimator.MaxContext}).[/]");
            }
        }
        else if (usableVramMb is int vramBudget)
        {
            int recommendedContext = MemoryEstimator.RecommendContext(model, vramBudget, kvPrecision);
            numCtx = options.Interactive
                ? PromptContextWindow(model, recommendedContext, kvPrecision, usableVramMb, usableRamMb)
                : recommendedContext;
        }
        else
        {
            // No VRAM signal (CPU-only or an undetected APU): keep the safe flat default.
            numCtx = model.RecommendedNumCtx;
        }

        RenderMemoryPlan(model, numCtx, kvPrecision, usableVramMb, usableRamMb);

        // 5. Download base model
        if (!options.SkipPull)
        {
            AnsiConsole.MarkupLineInterpolated($"\nFetching [blue]{modelReference}[/] (~{model.ApproxDiskGb:0.0} GB)...");
            int downloadResult = await runtime.DownloadAsync(model, AnsiConsole.WriteLine, cancellationToken).ConfigureAwait(false);

            if (downloadResult != 0)
            {
                AnsiConsole.MarkupLine("[red]error:[/] model download failed.");
                return 4;
            }
        }

        // 6. Prepare (apply context size); returns the id Copilot should request
        AnsiConsole.MarkupLineInterpolated($"\nPreparing model (context {numCtx} tokens)...");
        string modelId;
        try
        {
            modelId = await runtime.PrepareAsync(model, numCtx, AnsiConsole.WriteLine, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {exception.Message}");
            return 5;
        }

        // 7. runtime-specific performance tuning (best effort)
        if (options.Optimize)
        {
            runtime.ApplyRecommendedSettings(message => AnsiConsole.MarkupLineInterpolated($"[grey]{message}[/]"));
        }

        // 8. Save config. Token limits are derived from the context we baked in, so Copilot
        // sizes requests to the model instead of assuming a 128K window (and overflowing).
        (int maxPrompt, int maxOutput) = ResolveTokenLimits(options.MaxPromptTokens, options.MaxOutputTokens, numCtx);
        CodeLocalConfig config = new CodeLocalConfig
        {
            BaseUrl = runtime.OpenAiBaseUrl,
            Model = modelId,
            WireApi = options.Wire,
            RuntimeKey = runtime.Key,
            MaxPromptTokens = maxPrompt,
            MaxOutputTokens = maxOutput,
        };

        string persistNote = SaveConfigAndRender(options, config);

        // 9. Smoke test
        if (!options.SkipPull)
        {
            AnsiConsole.MarkupLine("\nRunning a quick smoke test...");

            try
            {
                string? smokeTestReply = await runtime.SmokeTestAsync(modelId, "Reply with exactly: Code.Local OK", cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(smokeTestReply))
                {
                    AnsiConsole.MarkupLineInterpolated($"[green]Model responded:[/] {smokeTestReply.Trim()}");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Smoke test returned no content (model may still be loading).[/]");
                }
            }
            catch (Exception exception)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]Smoke test skipped:[/] {exception.Message}");
            }
        }

        string body = options.Persist
            ? $"[green]Done.[/]{Markup.Escape(persistNote)}\n\nStart coding:  [blue]codelocal copilot[/]\n\n[grey]Env vars persisted: a new shell's plain `copilot` will also use local mode.[/]"
            : $"[green]Done.[/] Config saved to {Markup.Escape(ConfigStore.ConfigPath())}.\n\nStart coding:  [blue]codelocal copilot[/]";
        WriteDonePanel(body);

        return 0;
    }

    /// <summary>
    /// Client (gateway) mode: point Copilot at an existing OpenAI-compatible endpoint
    /// without provisioning a local runtime. Ensures the Copilot CLI is present and
    /// writes the provider config. This is what the generated team installer invokes.
    /// </summary>
    private static async Task<int> RunGatewayAsync(InitOptions options, CancellationToken cancellationToken)
    {
        string endpoint = options.Endpoint ?? string.Empty;
        string model = options.Model ?? string.Empty;

        if (string.IsNullOrWhiteSpace(model))
        {
            AnsiConsole.MarkupLine("[red]error:[/] --model is required with --endpoint (the model name the endpoint serves).");
            return 2;
        }

        PrintBanner(options.Interactive);
        AnsiConsole.MarkupLineInterpolated(
            $"[grey]Client mode: pointing Copilot at an existing endpoint (no local runtime).[/]\n");

        // The only thing to install in client mode is the Copilot CLI itself.
        await EnsureCopilotInstalledAsync(options.Interactive, options.InstallDependencies, cancellationToken).ConfigureAwait(false);

        CodeLocalConfig config = new CodeLocalConfig
        {
            BaseUrl = endpoint,
            Model = model,
            ApiKey = options.ApiKey,
            WireApi = options.Wire,
            RuntimeKey = RemoteRuntime.RuntimeKey,
            MaxPromptTokens = options.MaxPromptTokens,
            MaxOutputTokens = options.MaxOutputTokens,
        };
        string persistNote = SaveConfigAndRender(options, config);

        string body = options.Persist
            ? $"[green]Done.[/]{Markup.Escape(persistNote)}\n\nStart coding:  [blue]codelocal copilot[/] (or plain [blue]copilot[/] in a new shell)\n\n[grey]Pointing at {Markup.Escape(endpoint)}.[/]"
            : $"[green]Done.[/] Config saved to {Markup.Escape(ConfigStore.ConfigPath())}.\n\nStart coding:  [blue]codelocal copilot[/]\n\n[grey]Pointing at {Markup.Escape(endpoint)}.[/]";
        WriteDonePanel(body);

        return 0;
    }

    /// <summary>
    /// Prints the Code.Local banner (the figlet art only when interactive) and tagline.
    /// </summary>
    private static void PrintBanner(bool interactive)
    {
        if (interactive)
        {
            AnsiConsole.Write(new FigletText("Code.Local").Color(Color.Teal));
        }

        AnsiConsole.MarkupLine("[grey]Local AI coding models for GitHub Copilot[/]\n");
    }

    /// <summary>
    /// Saves the resolved Copilot configuration, renders it as an environment-variable table,
    /// and applies it to the user environment when persistence was requested. Returns the
    /// persist note to append to the closing panel, or an empty string when not persisting.
    /// </summary>
    private static string SaveConfigAndRender(InitOptions options, CodeLocalConfig config)
    {
        ConfigStore.Save(config);

        Table configTable = new Table().Border(TableBorder.Rounded).AddColumn("Copilot variable").AddColumn("Value");

        foreach (KeyValuePair<string, string> pair in CopilotEnvironment.BuildEnvironmentVariables(config))
        {
            configTable.AddRow(Markup.Escape(pair.Key), Markup.Escape(pair.Value));
        }

        AnsiConsole.Write(configTable);

        if (options.Persist)
        {
            return " " + CopilotEnvironment.Apply(config);
        }

        return "";
    }

    /// <summary>
    /// Writes the closing "Done" panel with the Code.Local header.
    /// </summary>
    private static void WriteDonePanel(string body)
    {
        AnsiConsole.Write(new Panel(body)
            .Header("Code.Local")
            .Border(BoxBorder.Rounded));
    }

    /// <summary>
    /// Render a model as a plain-text selection choice (no markup, so model names with
    /// brackets can never break Spectre's prompt rendering), annotated with VRAM fit.
    /// </summary>
    private static string FormatModelChoice(CodingModel model, CodingModel recommended, int? vramMb)
    {
        string suffix = model == recommended ? "  (recommended)" : "";
        string fitText;
        if (vramMb is int vram)
        {
            fitText = vram >= model.MinVramMb ? "  - fits your GPU" : $"  - needs ~{model.MinVramMb / 1024.0:0.0} GB VRAM";
        }
        else
        {
            fitText = "";
        }

        return $"{model.DisplayName} · {model.Quantization} ({model.ParamSize}, ~{model.ApproxDiskGb:0.0} GB){suffix}{fitText}";
    }

    /// <summary>
    /// Split the context window into prompt + output budgets for Copilot's BYOK token
    /// limits, so the agent sizes requests to the model rather than assuming a 128K window
    /// (which would overflow the model and get truncated). Honors explicit
    /// --max-prompt-tokens / --max-output-tokens overrides.
    /// </summary>
    /// <param name="maxPromptOverride">Explicit prompt-token limit, or null to derive it.</param>
    /// <param name="maxOutputOverride">Explicit output-token limit, or null to derive it.</param>
    /// <param name="numCtx">The context window the model was prepared with.</param>
    private static (int MaxPrompt, int MaxOutput) ResolveTokenLimits(int? maxPromptOverride, int? maxOutputOverride, int numCtx)
    {
        int maxOutput = maxOutputOverride ?? Math.Clamp(numCtx / 4, 512, 8192);
        int maxPrompt = maxPromptOverride ?? Math.Max(2048, numCtx - maxOutput);
        return (maxPrompt, maxOutput);
    }

    // Keep a headroom slice of VRAM for the driver/desktop, and never assume the whole of
    // system RAM is free for offload.
    private const double DiscreteVramHeadroom = 0.90;
    private const double RamUsableFraction = 0.80;
    private const double UnifiedUsableFraction = 0.92;

    /// <summary>
    /// Translate detected hardware into a usable-VRAM budget (for staying on-GPU) and the
    /// RAM available to absorb offload. Unified-memory rigs share one pool, so VRAM is
    /// already discounted by the detector and system RAM isn't added on top of it.
    /// </summary>
    private static (int? UsableVramMb, long UsableRamMb) ComputeBudget(HardwareInfo hardware, int? effectiveVramMb)
    {
        if (effectiveVramMb is not int vram)
        {
            long ramOnly = hardware.TotalRamMb is long totalRam ? (long)(totalRam * RamUsableFraction) : 0L;
            return (null, ramOnly);
        }

        if (hardware.UnifiedMemory)
        {
            long combined = hardware.TotalRamMb is long totalRam ? (long)(totalRam * UnifiedUsableFraction) : vram;
            return (vram, Math.Max(0L, combined - vram));
        }

        int usableVram = (int)(vram * DiscreteVramHeadroom);
        long ramOffload = hardware.TotalRamMb is long ram ? (long)(ram * RamUsableFraction) : 0L;

        return (usableVram, ramOffload);
    }

    /// <summary>
    /// Let the user pick a context window from the ladder with a selector (like the model
    /// picker), each row showing the estimated footprint and VRAM fit. The highlight starts
    /// at the 32K floor; the largest size that fits VRAM is marked "(recommended)". Arbitrary
    /// values are still available via --ctx.
    /// </summary>
    private static int PromptContextWindow(
        CodingModel model, int recommendedContext, KvCachePrecision precision, int? usableVramMb, long usableRamMb)
    {
        AnsiConsole.MarkupLine("\n[grey]Larger context = more code in view, but more VRAM (KV cache grows with it).[/]");

        return AnsiConsole.Prompt(
            new SelectionPrompt<int>()
                .Title("Select a [green]context window[/]:")
                .PageSize(10)
                .UseConverter(context => DescribeContextChoice(model, context, recommendedContext, precision, usableVramMb, usableRamMb))
                .AddChoices(MemoryEstimator.ContextLadder));
    }

    /// <summary>
    /// One selector row: token count, human-readable size, estimated total memory, the
    /// VRAM-fit verdict (colored), and a "(recommended)" marker for the auto-sized pick.
    /// </summary>
    private static string DescribeContextChoice(
        CodingModel model, int context, int recommendedContext, KvCachePrecision precision, int? usableVramMb, long usableRamMb)
    {
        MemoryEstimate estimate = MemoryEstimator.Estimate(model, context, precision);
        MemoryFit fit = MemoryEstimator.Classify(estimate.TotalMb, usableVramMb, usableRamMb);
        string recommendedMarker = context == recommendedContext ? "  [green](recommended)[/]" : "";

        return $"{context} ({context / 1024}K, ~{estimate.TotalGb:0.0} GB)  [{FitColor(fit)}]{FitLabel(fit)}[/]{recommendedMarker}";
    }

    /// <summary>
    /// Print the estimated memory breakdown for the chosen context and a colored verdict on
    /// whether it stays in VRAM, offloads to system RAM, or won't fit at all.
    /// </summary>
    private static void RenderMemoryPlan(
        CodingModel model, int numCtx, KvCachePrecision precision, int? usableVramMb, long usableRamMb)
    {
        MemoryEstimate estimate = MemoryEstimator.Estimate(model, numCtx, precision);
        MemoryFit fit = MemoryEstimator.Classify(estimate.TotalMb, usableVramMb, usableRamMb);

        Table table = new Table().Border(TableBorder.Rounded)
            .AddColumn($"Estimated memory @ {numCtx.ToString(CultureInfo.InvariantCulture)} ctx")
            .AddColumn("Size");
        table.AddRow("Model weights", $"~{estimate.WeightsGb:0.0} GB");
        table.AddRow($"KV cache ({(precision == KvCachePrecision.Q8 ? "q8" : "f16")})", $"~{estimate.KvCacheGb:0.0} GB");
        table.AddRow("Runtime overhead", $"~{estimate.OverheadGb:0.0} GB");
        table.AddRow("[bold]Total[/]", $"[bold]~{estimate.TotalGb:0.0} GB[/]");

        if (usableVramMb is int usableVram)
        {
            table.AddRow("Usable VRAM", $"~{usableVram / 1024.0:0.0} GB");
        }

        AnsiConsole.Write(table);

        double usableVramGb = (usableVramMb ?? 0) / 1024.0;
        double offloadGb = Math.Max(0, estimate.TotalMb - (usableVramMb ?? 0)) / 1024.0;
        string fitMessage = fit switch
        {
            MemoryFit.Vram =>
                $"[green]Fits in VRAM[/] (~{estimate.TotalGb:0.0} GB of ~{usableVramGb:0.0} GB usable) - no CPU offload.",
            MemoryFit.Offload =>
                $"[yellow]warning:[/] ~{estimate.TotalGb:0.0} GB exceeds ~{usableVramGb:0.0} GB usable VRAM; ~{offloadGb:0.0} GB "
                + "will offload to system RAM (slower). Lower the context or pick a smaller model to stay fully on GPU.",
            MemoryFit.TooBig =>
                $"[red]warning:[/] ~{estimate.TotalGb:0.0} GB exceeds VRAM + system RAM - this likely won't run. "
                + "Pick a smaller model or a shorter context.",
            _ =>
                "[grey]No GPU VRAM detected - the model will run on CPU/system RAM; expect slower responses.[/]",
        };
        AnsiConsole.MarkupLine(fitMessage);
    }

    /// <summary>
    /// Returns the markup color for a memory fit verdict.
    /// </summary>
    private static string FitColor(MemoryFit fit)
    {
        return fit switch
        {
            MemoryFit.Vram => "green",
            MemoryFit.Offload => "yellow",
            MemoryFit.TooBig => "red",
            _ => "grey",
        };
    }

    /// <summary>
    /// Returns a human-readable label for a memory fit verdict.
    /// </summary>
    private static string FitLabel(MemoryFit fit)
    {
        return fit switch
        {
            MemoryFit.Vram => "fits VRAM",
            MemoryFit.Offload => "offloads to RAM",
            MemoryFit.TooBig => "won't fit",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Ensure the GitHub Copilot CLI is installed so `codelocal copilot` can launch it.
    /// Best effort and non-fatal: a failure or decline doesn't stop init (the launcher
    /// offers the install again on demand).
    /// </summary>
    private static async Task EnsureCopilotInstalledAsync(bool interactive, bool installRequested, CancellationToken cancellationToken)
    {
        CopilotCli copilot = new CopilotCli();

        if (copilot.IsInstalled)
        {
            return;
        }

        await DependencyInstallationManager.EnsureInstalledAsync(
            "GitHub Copilot CLI", copilot.IsInstalled, copilot.Installer, interactive, installRequested, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensure the runtime is installed AND its server is reachable. Offers a consent-gated
    /// (re)install when it's missing or broken (e.g. an orphaned binary left by a partial
    /// uninstall). Returns 0 to proceed, or a non-zero exit code to stop init.
    /// </summary>
    private static async Task<int> EnsureRuntimeReadyAsync(
        IModelRuntime runtime, bool interactive, bool installRequested, CancellationToken cancellationToken)
    {
        // Install the runtime binary if it isn't present.
        if (!runtime.IsInstalled)
        {
            if (runtime.Installer is null)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"\n[yellow]{runtime.Name} is not installed[/] and has no automatic installer. Install it manually, then re-run `codelocal init`.");
                return 3;
            }

            bool installed = await DependencyInstallationManager.EnsureInstalledAsync(
                runtime.Name, runtime.IsInstalled, runtime.Installer, interactive, installRequested, cancellationToken).ConfigureAwait(false);

            if (!installed)
            {
                return 3;
            }
        }

        // The binary exists — make sure the server actually comes up (start it if needed).
        if (await runtime.EnsureServerRunningAsync(AnsiConsole.WriteLine, cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        // Installed but the server won't start: usually a broken or partial install.
        AnsiConsole.MarkupLineInterpolated(
            $"\n[yellow]{runtime.Name} is installed but its server isn't responding[/] — this usually means a broken or partial install.");

        IRuntimeInstaller? installer = runtime.Installer;

        if (installer is not null)
        {
            bool consent = interactive
                ? AnsiConsole.Confirm($"Reinstall {runtime.Name} now?")
                : installRequested;

            if (consent)
            {
                AnsiConsole.MarkupLineInterpolated($"\nReinstalling {runtime.Name}...");
                await installer.InstallAsync(AnsiConsole.WriteLine, cancellationToken).ConfigureAwait(false);

                if (await runtime.EnsureServerRunningAsync(AnsiConsole.WriteLine, cancellationToken).ConfigureAwait(false))
                {
                    return 0;
                }
            }
        }

        string manualHint = installer?.ManualInstallHint ?? $"Reinstall {runtime.Name} manually and retry from a new terminal.";
        AnsiConsole.MarkupLineInterpolated(
            $"[red]error:[/] {runtime.Name} server could not be started. {manualHint}");

        return 3;
    }
}
