using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeLocal.Runtimes.Ollama.Models;

/// <summary>
/// Response body of GET /api/ps (models currently loaded). <c>size_vram</c> vs <c>size</c>
/// reveals how much of a model is resident in VRAM vs offloaded to system RAM/CPU.
/// </summary>
internal sealed class OllamaPsResponse
{
    /// <summary>
    /// The models currently loaded in memory.
    /// </summary>
    [JsonPropertyName("models")]
    public List<OllamaPsModel> Models { get; set; } = new List<OllamaPsModel>();
}
