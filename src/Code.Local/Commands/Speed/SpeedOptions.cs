using System.Collections.Generic;

namespace CodeLocal.Commands.Speed;

/// <summary>
/// Strongly-typed options for <c>codelocal speed</c>. Runtime and model fall back to the
/// saved configuration when not supplied, so those are left null here and resolved by the
/// command itself.
/// </summary>
public sealed class SpeedOptions
{
    /// <summary>
    /// Runtime/back-end key the user explicitly requested, or null to use the saved config.
    /// </summary>
    public string? RuntimeKey { get; init; }

    /// <summary>
    /// Model id the user explicitly requested, or null to use the saved config.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Number of measured runs to average, or null to use the command's default.
    /// </summary>
    public int? Runs { get; init; }

    /// <summary>
    /// Tokens to generate per run, or null to use the command's default.
    /// </summary>
    public int? Tokens { get; init; }

    /// <summary>
    /// Binds the speed options from the raw parsed command-line dictionary.
    /// </summary>
    /// <param name="rawOptions">The raw <c>--key value</c> pairs parsed from the command line.</param>
    public static SpeedOptions Parse(IReadOnlyDictionary<string, string> rawOptions)
    {
        CommandLineOptions options = new CommandLineOptions(rawOptions);
        SpeedOptions parsed = new SpeedOptions
        {
            RuntimeKey = options.Text("runtime"),
            Model = options.Text("model"),
            Runs = options.PositiveInteger("runs"),
            Tokens = options.PositiveInteger("tokens"),
        };
        options.EnsureNoUnknownOptions();

        return parsed;
    }
}
