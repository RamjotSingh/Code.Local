namespace CodeLocal.Services.Models;

/// <summary>
/// A human-readable assessment of whether GPU acceleration is ready for Ollama.
/// </summary>
public sealed class GpuReadiness
{
    /// <summary>
    /// Severity of the assessment.
    /// </summary>
    public GpuReadinessLevel Level { get; init; }

    /// <summary>
    /// Human-readable message describing the readiness state and any action needed.
    /// </summary>
    public string Message { get; init; } = "";
}
