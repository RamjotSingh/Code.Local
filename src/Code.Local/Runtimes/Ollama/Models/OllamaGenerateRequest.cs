using System.Text.Json.Serialization;

namespace CodeLocal.Runtimes.Ollama.Models;

/// <summary>
/// Request body of POST /api/generate (non-streaming).
/// </summary>
internal sealed class OllamaGenerateRequest
{
    /// <summary>
    /// The model to run.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    /// <summary>
    /// The prompt to generate from.
    /// </summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    /// <summary>
    /// Whether to stream tokens; Code.Local uses non-streaming requests.
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    /// <summary>
    /// How long to keep the model resident after the request (-1 = indefinitely).
    /// </summary>
    [JsonPropertyName("keep_alive")]
    public int? KeepAlive { get; set; }

    /// <summary>
    /// Optional generation options (token limit, temperature, ...).
    /// </summary>
    [JsonPropertyName("options")]
    public OllamaGenerateOptions? Options { get; set; }
}
