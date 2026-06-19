namespace CodeLocal.Services.Models;

/// <summary>
/// How a model + context choice fits the available memory.
/// </summary>
public enum MemoryFit
{
    /// <summary>
    /// Fits entirely in (usable) VRAM - no CPU offload.
    /// </summary>
    Vram,

    /// <summary>
    /// Exceeds VRAM but fits in VRAM + system RAM - Ollama offloads the remainder (slower).
    /// </summary>
    Offload,

    /// <summary>
    /// Exceeds VRAM + system RAM - won't run.
    /// </summary>
    TooBig,

    /// <summary>
    /// No GPU VRAM detected - fit can't be assessed.
    /// </summary>
    Unknown,
}
