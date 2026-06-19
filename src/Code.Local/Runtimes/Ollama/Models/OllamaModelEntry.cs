using System.Text.Json.Serialization;

namespace CodeLocal.Runtimes.Ollama.Models;

/// <summary>
/// A single installed model entry returned by Ollama.
/// </summary>
internal sealed class OllamaModelEntry
{
    /// <summary>
    /// The model's tag/name (e.g. "qwen3.5:4b").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// On-disk size of the model, in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }
}
