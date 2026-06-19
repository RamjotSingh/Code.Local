using System.Collections.Generic;
using System.IO;

namespace CodeLocal.Commands.Package;

/// <summary>
/// Strongly-typed options for <c>codelocal package</c>, which generates a team installer
/// that points another machine's Copilot at a shared endpoint.
/// </summary>
public sealed class PackageOptions
{
    /// <summary>
    /// Shared OpenAI-compatible endpoint the installer points Copilot at (required).
    /// </summary>
    public string Endpoint { get; init; } = "";

    /// <summary>
    /// Model name the endpoint serves (required).
    /// </summary>
    public string Model { get; init; } = "";

    /// <summary>
    /// API key baked into the installer (default <c>codelocal</c>).
    /// </summary>
    public string ApiKey { get; init; } = "codelocal";

    /// <summary>
    /// Copilot wire API baked into the installer (default <c>completions</c>).
    /// </summary>
    public string Wire { get; init; } = "completions";

    /// <summary>
    /// Output directory for the generated installer files.
    /// </summary>
    public string OutputDirectory { get; init; } = "";

    /// <summary>
    /// Which installer(s) to emit: <c>all</c>, <c>windows</c>, <c>macos</c>, or <c>linux</c>.
    /// </summary>
    public string OperatingSystem { get; init; } = "all";

    /// <summary>
    /// Optional prompt-token limit baked into the installer.
    /// </summary>
    public int? MaxPromptTokens { get; init; }

    /// <summary>
    /// Optional output-token limit baked into the installer.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Binds the package options from the raw parsed command-line dictionary.
    /// </summary>
    /// <param name="rawOptions">The raw <c>--key value</c> pairs parsed from the command line.</param>
    public static PackageOptions Parse(IReadOnlyDictionary<string, string> rawOptions)
    {
        CommandLineOptions options = new CommandLineOptions(rawOptions);
        PackageOptions parsed = new PackageOptions
        {
            Endpoint = options.Text("endpoint") ?? "",
            Model = options.Text("model") ?? "",
            ApiKey = options.Text("api-key") ?? "codelocal",
            Wire = options.Text("wire") ?? "completions",
            OutputDirectory = options.Text("out")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "codelocal-installer"),
            OperatingSystem = (options.Text("os") ?? "all").ToLowerInvariant(),
            MaxPromptTokens = options.PositiveInteger("max-prompt-tokens"),
            MaxOutputTokens = options.PositiveInteger("max-output-tokens"),
        };
        options.EnsureNoUnknownOptions();

        return parsed;
    }
}
