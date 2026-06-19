namespace CodeLocal.Services.Models;

/// <summary>
/// The KV-cache element precision Ollama is configured to use. Code.Local defaults to
/// q8_0 (half the footprint of fp16) when performance tuning is enabled.
/// </summary>
public enum KvCachePrecision
{
    /// <summary>
    /// 16-bit KV cache (Ollama's default when flash attention / q8 are not set).
    /// </summary>
    Fp16,

    /// <summary>
    /// 8-bit (q8_0) KV cache - half the footprint; Code.Local's tuned default.
    /// </summary>
    Q8,
}
