namespace CodeLocal.Services.Models;

/// <summary>
/// Severity of a GPU-acceleration readiness assessment.
/// </summary>
public enum GpuReadinessLevel
{
    /// <summary>
    /// GPU acceleration is ready to use.
    /// </summary>
    Ready,

    /// <summary>
    /// Usable, but something should be addressed (e.g. an outdated driver).
    /// </summary>
    Warning,

    /// <summary>
    /// Informational note only.
    /// </summary>
    Info,
}
