using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeLocal.Runtimes.Ollama.Models;

/// <summary>
/// Response body of GET /api/tags.
/// </summary>
internal sealed class OllamaTagsResponse
{
    /// <summary>
    /// The models currently pulled into Ollama.
    /// </summary>
    [JsonPropertyName("models")]
    public List<OllamaModelEntry> Models { get; set; } = new List<OllamaModelEntry>();
}
