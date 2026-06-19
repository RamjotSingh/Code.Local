using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Models;
using CodeLocal.Runtimes;
using CodeLocal.Runtimes.Models;
using CodeLocal.Services;
using Spectre.Console;

namespace CodeLocal.Commands.Speed;

/// <summary>
/// Benchmarks the configured local model's generation speed. Warms the model up first (so a
/// cold weight-load doesn't skew the numbers), then averages a few timed runs and reports
/// decode/prefill tokens-per-second plus the GPU/CPU placement.
/// </summary>
public static class SpeedCommand
{
    private const string BenchPrompt =
        "Write a Python function that returns the nth Fibonacci number, then explain how it works step by step.";
    private const int DefaultRuns = 3;
    private const int DefaultTokens = 256;
    private const int WarmupTokens = 16;

    public static async Task<int> RunAsync(SpeedOptions options, CancellationToken cancellationToken)
    {
        CodeLocalConfig? config = ConfigStore.LoadOrNull();
        string? runtimeKey = options.RuntimeKey ?? config?.RuntimeKey;
        IModelRuntime runtime = RuntimeFactory.Create(runtimeKey);

        string? model = options.Model ?? config?.Model;

        if (string.IsNullOrWhiteSpace(model))
        {
            AnsiConsole.MarkupLine("[red]error:[/] no model configured. Run `codelocal init` first, or pass --model <id>.");
            return 2;
        }

        if (!runtime.IsInstalled)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {runtime.Name} isn't installed. Run `codelocal init` first.");
            return 2;
        }

        bool serverReady = await runtime
            .EnsureServerRunningAsync(message => AnsiConsole.MarkupLineInterpolated($"[grey]{message}[/]"), cancellationToken)
            .ConfigureAwait(false);

        if (!serverReady)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] the {runtime.Name} server isn't reachable.");
            return 3;
        }

        int runs = options.Runs ?? DefaultRuns;
        int tokens = options.Tokens ?? DefaultTokens;

        AnsiConsole.MarkupLineInterpolated(
            $"Benchmarking [green]{model}[/] - warmup + {runs} run(s) x {tokens} tokens...\n");

        double coldLoadSeconds;
        List<GenerationMetrics> benchmarkSamples = new List<GenerationMetrics>(runs);

        try
        {
            // 1. Warmup: load weights into memory and warm the compute kernels. Its
            //    load_duration is the cold-load cost; its speed is discarded.
            AnsiConsole.MarkupLine("[grey]Warming up (loading model)...[/]");
            GenerationMetrics warmupMetrics = await runtime.BenchmarkAsync(model, BenchPrompt, WarmupTokens, cancellationToken).ConfigureAwait(false);
            coldLoadSeconds = warmupMetrics.LoadSeconds;

            // 2. Measured runs against the now-resident model.
            for (int runIndex = 0; runIndex < runs; runIndex++)
            {
                AnsiConsole.MarkupLineInterpolated($"[grey]Run {runIndex + 1}/{runs}...[/]");
                benchmarkSamples.Add(await runtime.BenchmarkAsync(model, BenchPrompt, tokens, cancellationToken).ConfigureAwait(false));
            }
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] benchmark failed: {exception.Message}");
            return 4;
        }

        string? placement = await runtime.GetPlacementAsync(model, cancellationToken).ConfigureAwait(false);

        double averageDecode = benchmarkSamples.Average(sample => sample.DecodeTokensPerSecond);
        double minimumDecode = benchmarkSamples.Min(sample => sample.DecodeTokensPerSecond);
        double maximumDecode = benchmarkSamples.Max(sample => sample.DecodeTokensPerSecond);
        double averagePrefill = benchmarkSamples.Average(sample => sample.PrefillTokensPerSecond);
        int generatedTokens = benchmarkSamples[benchmarkSamples.Count - 1].GeneratedTokens;

        Table resultsTable = new Table().Border(TableBorder.Rounded).AddColumn("Speed").AddColumn("Value");
        resultsTable.AddRow("Model", Markup.Escape(model));
        resultsTable.AddRow("Placement", placement is null ? "[grey]unknown[/]" : Markup.Escape(placement));
        resultsTable.AddRow("Model load (cold)", coldLoadSeconds < 0.05 ? "already resident" : $"{coldLoadSeconds:0.0} s");
        resultsTable.AddRow("Prefill", $"~{averagePrefill:0} tok/s");
        resultsTable.AddRow("Generation",
            runs > 1
                ? $"~{averageDecode:0} tok/s  (range {minimumDecode:0}-{maximumDecode:0}, {generatedTokens} tok/run)"
                : $"~{averageDecode:0} tok/s  ({generatedTokens} tok)");
        AnsiConsole.Write(resultsTable);

        if (placement is not null && placement.Contains("CPU", StringComparison.Ordinal)
            && !placement.StartsWith("100% GPU", StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine(
                "[yellow]note:[/] part of the model is on the CPU, where generation is bandwidth-bound and much slower. "
                + "A smaller model, a shorter context, or a lighter quant would keep it fully on the GPU "
                + "(re-run `codelocal init` to resize).");
        }

        return 0;
    }
}
