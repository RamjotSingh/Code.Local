namespace CodeLocal.Runtimes.Models;

/// <summary>
/// Phase timings for a single generation, split so a benchmark can isolate steady-state
/// decode speed from one-time model-load cost and prompt prefill. Durations are seconds.
/// </summary>
public sealed class GenerationMetrics
{
    /// <summary>
    /// Number of prompt (prefill) tokens evaluated.
    /// </summary>
    public int PromptTokens { get; init; }

    /// <summary>
    /// Wall-clock seconds spent on prompt prefill.
    /// </summary>
    public double PromptSeconds { get; init; }

    /// <summary>
    /// Number of generated (decode) tokens.
    /// </summary>
    public int GeneratedTokens { get; init; }

    /// <summary>
    /// Wall-clock seconds spent generating (decode only, excludes load).
    /// </summary>
    public double GenerateSeconds { get; init; }

    /// <summary>
    /// Wall-clock seconds spent loading the model (~0 when already resident).
    /// </summary>
    public double LoadSeconds { get; init; }

    /// <summary>
    /// Steady-state generation (decode) speed in tokens/second - independent of model load.
    /// </summary>
    public double DecodeTokensPerSecond => GenerateSeconds > 0 ? GeneratedTokens / GenerateSeconds : 0;

    /// <summary>
    /// Prompt processing (prefill) speed in tokens/second.
    /// </summary>
    public double PrefillTokensPerSecond => PromptSeconds > 0 ? PromptTokens / PromptSeconds : 0;
}
