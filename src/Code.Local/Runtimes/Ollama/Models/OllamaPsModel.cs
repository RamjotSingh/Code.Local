using System.Text.Json.Serialization;

namespace CodeLocal.Runtimes.Ollama.Models;

/// <summary>
/// A single running-model entry returned by GET /api/ps.
/// </summary>
internal sealed class OllamaPsModel
{
    /// <summary>
    /// The model's display name/tag.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// The underlying model identifier.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    /// <summary>
    /// Total size of the loaded model, in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Portion of the model resident in VRAM, in bytes.
    /// </summary>
    [JsonPropertyName("size_vram")]
    public long SizeVram { get; set; }
}
