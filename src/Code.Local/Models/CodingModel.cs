using System.Collections.Generic;

namespace CodeLocal.Models;

/// <summary>
/// A curated local coding model that Code.Local knows how to install and tune. Model
/// references are per-runtime (an Ollama tag, a Hugging Face repo, …) keyed by the
/// runtime's key, so the same logical model can map to multiple runtimes.
/// </summary>
public sealed class CodingModel
{
    /// <summary>
    /// Stable identifier; also the local (derived) model name and the COPILOT_MODEL value.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// Human-friendly name shown in the selector (e.g. "Qwen3.5 4B").
    /// </summary>
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// Parameter-count label (e.g. "4B", "30B MoE").
    /// </summary>
    public string ParamSize { get; init; } = "";

    /// <summary>
    /// Weight quantization (e.g. "Q4_K_M", "Q8", "int4", "MXFP4", "QAT int4").
    /// </summary>
    public string Quantization { get; init; } = "";

    /// <summary>
    /// Suggested minimum VRAM (MB) to run at the context floor without heavy offload.
    /// </summary>
    public int MinVramMb { get; init; }

    /// <summary>
    /// Fallback context window (tokens) used when VRAM can't be detected.
    /// </summary>
    public int RecommendedNumCtx { get; init; }

    /// <summary>
    /// Approximate KV-cache bytes per token (K+V at fp16) for this architecture. Used to
    /// estimate memory and size the context window to fit VRAM. A planning estimate derived
    /// from a representative architecture, not an exact per-build figure.
    /// </summary>
    public int KvBytesPerTokenFp16 { get; init; }

    /// <summary>
    /// Approximate on-disk / resident weight size, in GB.
    /// </summary>
    public double ApproxDiskGb { get; init; }

    /// <summary>
    /// License identifier (e.g. "Apache-2.0", "Gemma").
    /// </summary>
    public string License { get; init; } = "";

    /// <summary>
    /// One-line description shown to the user.
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Per-runtime model references, keyed by runtime key (e.g. "ollama" -> "qwen3.5:4b").
    /// </summary>
    public IReadOnlyDictionary<string, string> Sources { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The runtime-specific model reference for the given runtime key, or null if the
    /// model isn't available on that runtime.
    /// </summary>
    public string? SourceFor(string runtimeKey)
    {
        if (Sources.TryGetValue(runtimeKey, out string? sourceValue))
        {
            return sourceValue;
        }

        return null;
    }
}
