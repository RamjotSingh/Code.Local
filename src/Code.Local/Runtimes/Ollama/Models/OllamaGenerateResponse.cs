using System.Text.Json.Serialization;

namespace CodeLocal.Runtimes.Ollama.Models;

/// <summary>
/// Response body of POST /api/generate (non-streaming). Durations are nanoseconds; counts
/// and durations let a caller compute prefill and decode token rates independently of the
/// one-time model load.
/// </summary>
internal sealed class OllamaGenerateResponse
{
    /// <summary>
    /// The generated text.
    /// </summary>
    [JsonPropertyName("response")]
    public string? Response { get; set; }

    /// <summary>
    /// Total request time, in nanoseconds.
    /// </summary>
    [JsonPropertyName("total_duration")]
    public long TotalDuration { get; set; }

    /// <summary>
    /// Time spent loading the model, in nanoseconds (~0 when already resident).
    /// </summary>
    [JsonPropertyName("load_duration")]
    public long LoadDuration { get; set; }

    /// <summary>
    /// Number of prompt (prefill) tokens evaluated.
    /// </summary>
    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; set; }

    /// <summary>
    /// Time spent on prompt prefill, in nanoseconds.
    /// </summary>
    [JsonPropertyName("prompt_eval_duration")]
    public long PromptEvalDuration { get; set; }

    /// <summary>
    /// Number of generated (decode) tokens.
    /// </summary>
    [JsonPropertyName("eval_count")]
    public int EvalCount { get; set; }

    /// <summary>
    /// Time spent generating, in nanoseconds.
    /// </summary>
    [JsonPropertyName("eval_duration")]
    public long EvalDuration { get; set; }
}
