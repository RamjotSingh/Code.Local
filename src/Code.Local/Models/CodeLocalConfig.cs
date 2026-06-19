using System.Text.Json.Serialization;

namespace CodeLocal.Models;

/// <summary>
/// The saved provider configuration: the OpenAI-compatible endpoint and model Code.Local
/// points a coding assistant at, plus the runtime backing that endpoint. This is
/// tool-agnostic data — a specific assistant integration (e.g. Copilot) decides how to hand
/// these values to its CLI.
/// </summary>
public sealed class CodeLocalConfig
{
    /// <summary>
    /// OpenAI-compatible base URL the assistant is pointed at. Required — every config records one.
    /// </summary>
    [JsonPropertyName("BaseUrl")]
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Model identifier the assistant requests. Required — every config records one.
    /// </summary>
    [JsonPropertyName("Model")]
    public required string Model { get; init; }

    /// <summary>
    /// API key for the provider; "ollama" for a local, keyless Ollama endpoint.
    /// </summary>
    [JsonPropertyName("ApiKey")]
    public string ApiKey { get; init; } = "ollama";

    /// <summary>
    /// Wire protocol the assistant uses: "completions" (default) or "responses".
    /// </summary>
    [JsonPropertyName("WireApi")]
    public string WireApi { get; init; } = "completions";

    /// <summary>
    /// Key of the runtime backing this config: a local runtime key (e.g. "ollama"), or
    /// "remote" for an existing remote endpoint. Required — every config records one, and a
    /// saved config missing it is treated as invalid rather than silently defaulted.
    /// </summary>
    [JsonPropertyName("RuntimeKey")]
    public required string RuntimeKey { get; init; }

    /// <summary>
    /// Maximum prompt tokens to advertise to the assistant, or null to leave unset.
    /// </summary>
    [JsonPropertyName("MaxPromptTokens")]
    public int? MaxPromptTokens { get; init; }

    /// <summary>
    /// Maximum output tokens to advertise to the assistant, or null to leave unset.
    /// </summary>
    [JsonPropertyName("MaxOutputTokens")]
    public int? MaxOutputTokens { get; init; }
}
