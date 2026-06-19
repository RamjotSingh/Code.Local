using System.Collections.Generic;

namespace CodeLocal.Commands.Init;

/// <summary>
/// Strongly-typed options for <c>codelocal init</c>, parsed once from the raw command line.
/// Covers both local-runtime setup and client mode (used when an endpoint is supplied).
/// </summary>
public sealed class InitOptions
{
    /// <summary>
    /// Whether to show the banner and interactive prompts. False under <c>--non-interactive</c>.
    /// </summary>
    public bool Interactive { get; init; }

    /// <summary>
    /// Whether to apply runtime-specific performance tuning. False under <c>--no-optimize</c>.
    /// </summary>
    public bool Optimize { get; init; }

    /// <summary>
    /// Whether to configure only, skipping the model download and smoke test. True under <c>--skip-pull</c>.
    /// </summary>
    public bool SkipPull { get; init; }

    /// <summary>
    /// Whether to also persist the configuration as user environment variables. True under <c>--persist</c>.
    /// </summary>
    public bool Persist { get; init; }

    /// <summary>
    /// Whether missing dependencies may be installed without an interactive prompt.
    /// True under <c>--auto-install-dependencies</c>.
    /// </summary>
    public bool InstallDependencies { get; init; }

    /// <summary>
    /// Copilot wire API: <c>completions</c> (default) or <c>responses</c>.
    /// </summary>
    public string Wire { get; init; } = "completions";

    /// <summary>
    /// Runtime/back-end key that hosts the model (default <c>ollama</c>).
    /// </summary>
    public string RuntimeKey { get; init; } = "ollama";

    /// <summary>
    /// Existing OpenAI-compatible endpoint for client mode, or null for local setup.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Model tag/id the user explicitly requested, or null to use the recommendation.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// API key written to the Copilot config in client mode (default <c>codelocal</c>).
    /// </summary>
    public string ApiKey { get; init; } = "codelocal";

    /// <summary>
    /// Manual VRAM override in megabytes (from <c>--vram</c> in GB), or null to use detection.
    /// </summary>
    public int? VramOverrideMb { get; init; }

    /// <summary>
    /// Explicit context-window override in tokens (from <c>--ctx</c>), or null to auto-size.
    /// </summary>
    public int? ContextWindow { get; init; }

    /// <summary>
    /// Explicit prompt-token limit override, or null to derive it from the context window.
    /// </summary>
    public int? MaxPromptTokens { get; init; }

    /// <summary>
    /// Explicit output-token limit override, or null to derive it from the context window.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Binds the init options from the raw parsed command-line dictionary.
    /// </summary>
    /// <param name="rawOptions">The raw <c>--key value</c> pairs parsed from the command line.</param>
    public static InitOptions Parse(IReadOnlyDictionary<string, string> rawOptions)
    {
        CommandLineOptions options = new CommandLineOptions(rawOptions);
        double? vramGigabytes = options.PositiveDouble("vram");
        InitOptions parsed = new InitOptions
        {
            Interactive = !options.HasFlag("non-interactive"),
            Optimize = !options.HasFlag("no-optimize"),
            SkipPull = options.HasFlag("skip-pull"),
            Persist = options.HasFlag("persist"),
            InstallDependencies = options.HasFlag("auto-install-dependencies"),
            Wire = options.Text("wire") ?? "completions",
            RuntimeKey = options.Text("runtime") ?? "ollama",
            Endpoint = options.Text("endpoint")?.Trim(),
            Model = options.Text("model"),
            ApiKey = options.Text("api-key") ?? "codelocal",
            VramOverrideMb = vramGigabytes is double gigabytes ? (int)(gigabytes * 1024) : null,
            ContextWindow = options.PositiveInteger("ctx"),
            MaxPromptTokens = options.PositiveInteger("max-prompt-tokens"),
            MaxOutputTokens = options.PositiveInteger("max-output-tokens"),
        };
        options.EnsureNoUnknownOptions();

        return parsed;
    }
}
