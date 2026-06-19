using System.Text.Json.Serialization;

namespace CodeLocal.Runtimes.Ollama.Models;

/// <summary>
/// Generation options (a subset) passed inside <see cref="OllamaGenerateRequest"/>.
/// </summary>
internal sealed class OllamaGenerateOptions
{
    /// <summary>
    /// Maximum number of tokens to generate.
    /// </summary>
    [JsonPropertyName("num_predict")]
    public int NumPredict { get; set; }

    /// <summary>
    /// Sampling temperature (0 = deterministic).
    /// </summary>
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }
}
