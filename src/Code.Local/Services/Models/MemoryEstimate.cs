namespace CodeLocal.Services.Models;

/// <summary>
/// A rough breakdown of the memory a model needs once loaded: resident weights, the KV
/// cache for the chosen context window, and runtime overhead (CUDA/Metal context plus
/// compute buffers). All figures are estimates for capacity planning, not exact
/// reservations.
/// </summary>
public sealed class MemoryEstimate
{
    /// <summary>
    /// Resident model weights (approximately the on-disk GGUF size), in MB.
    /// </summary>
    public int WeightsMb { get; init; }

    /// <summary>
    /// KV cache for the chosen context window, in MB.
    /// </summary>
    public int KvCacheMb { get; init; }

    /// <summary>
    /// Runtime overhead (CUDA/Metal context + compute buffers), in MB.
    /// </summary>
    public int OverheadMb { get; init; }

    /// <summary>
    /// Total estimated resident memory, in MB.
    /// </summary>
    public int TotalMb => WeightsMb + KvCacheMb + OverheadMb;

    /// <summary>
    /// Total estimated resident memory, in GB.
    /// </summary>
    public double TotalGb => TotalMb / 1024.0;

    /// <summary>
    /// Model weights, in GB.
    /// </summary>
    public double WeightsGb => WeightsMb / 1024.0;

    /// <summary>
    /// KV cache, in GB.
    /// </summary>
    public double KvCacheGb => KvCacheMb / 1024.0;

    /// <summary>
    /// Runtime overhead, in GB.
    /// </summary>
    public double OverheadGb => OverheadMb / 1024.0;
}
