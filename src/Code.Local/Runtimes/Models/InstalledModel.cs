namespace CodeLocal.Runtimes.Models;

/// <summary>
/// A model already present on a runtime (shown by `status`).
/// </summary>
public sealed class InstalledModel
{
    /// <summary>
    /// The model's tag/name as reported by the runtime.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// On-disk size of the model, in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// On-disk size of the model, in GB.
    /// </summary>
    public double SizeGb => SizeBytes / 1_000_000_000.0;
}
