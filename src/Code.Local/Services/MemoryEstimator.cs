using System;
using System.Collections.Generic;
using CodeLocal.Models;
using CodeLocal.Services.Models;

namespace CodeLocal.Services;

/// <summary>
/// Estimates how much memory a model + context window needs and picks a context size that
/// fits a memory budget. Lets `init` size the context to the GPU (avoiding silent CPU
/// offload) instead of using a flat default, and warn when a choice spills into system RAM.
/// </summary>
public static class MemoryEstimator
{
    /// <summary>
    /// Context sizes offered in the interactive chooser and scanned when auto-sizing, from
    /// the 32K floor (less is too little for real coding context) to the catalog models'
    /// 256K ceiling.
    /// </summary>
    public static readonly IReadOnlyList<int> ContextLadder = new[]
    {
        32768, 49152, 65536, 98304, 131072, 196608, 262144,
    };

    /// <summary>
    /// Smallest context Code.Local will configure. Below ~32K there isn't room for real
    /// coding context (open files + tools + history), so we never go lower - even when that
    /// means offloading some KV cache to system RAM on a small GPU.
    /// </summary>
    public const int MinContext = 32768;

    /// <summary>
    /// Largest context Code.Local will configure (the catalog models' 256K ceiling).
    /// </summary>
    public const int MaxContext = 262144;

    // CUDA/Metal context + runtime baseline, before per-model compute buffers.
    private const int RuntimeBaseMb = 500;

    /// <summary>
    /// Estimate the resident memory for <paramref name="model"/> at <paramref name="numCtx"/>
    /// tokens of context with the given KV-cache precision.
    /// </summary>
    /// <param name="model">The model to estimate memory for.</param>
    /// <param name="numCtx">Context window size in tokens.</param>
    /// <param name="precision">KV-cache precision (Q8 or FP16).</param>
    public static MemoryEstimate Estimate(CodingModel model, int numCtx, KvCachePrecision precision)
    {
        int weightsMb = (int)Math.Round(model.ApproxDiskGb * 1024.0);

        double kvScale = precision == KvCachePrecision.Q8 ? 0.5 : 1.0;
        long kvBytes = (long)Math.Round((double)model.KvBytesPerTokenFp16 * numCtx * kvScale);
        int kvCacheMb = (int)(kvBytes / (1024L * 1024L));

        // Compute buffers scale a little with both weights and context length.
        int overheadMb = RuntimeBaseMb + (int)Math.Round(weightsMb * 0.03) + (numCtx / 1024) * 8;

        return new MemoryEstimate { WeightsMb = weightsMb, KvCacheMb = kvCacheMb, OverheadMb = overheadMb };
    }

    /// <summary>
    /// Largest ladder context whose estimated footprint fits <paramref name="budgetMb"/>.
    /// Falls back to the smallest ladder entry (the 32K floor) when even that overflows, so
    /// we never recommend a context too small to code with (the caller warns about offload).
    /// </summary>
    /// <param name="model">The model to recommend a context size for.</param>
    /// <param name="budgetMb">Memory budget in megabytes.</param>
    /// <param name="precision">KV-cache precision (Q8 or FP16).</param>
    /// <param name="maxCtx">Maximum context size to consider (defaults to MaxContext).</param>
    public static int RecommendContext(
        CodingModel model, int budgetMb, KvCachePrecision precision, int maxCtx = MaxContext)
    {
        int bestContext = ContextLadder[0];

        foreach (int contextSize in ContextLadder)
        {
            if (contextSize > maxCtx)
            {
                break;
            }

            if (Estimate(model, contextSize, precision).TotalMb <= budgetMb)
            {
                bestContext = contextSize;
            }
        }

        return bestContext;
    }

    /// <summary>
    /// Classify how an estimated footprint fits the usable VRAM and the RAM available for
    /// offload. A null <paramref name="usableVramMb"/> means VRAM is unknown.
    /// </summary>
    /// <param name="totalMb">Total memory footprint in megabytes.</param>
    /// <param name="usableVramMb">Usable VRAM in megabytes, or null if unknown.</param>
    /// <param name="usableRamMb">Usable system RAM in megabytes for offload.</param>
    public static MemoryFit Classify(int totalMb, int? usableVramMb, long usableRamMb)
    {
        if (usableVramMb is not int vramMb)
        {
            return MemoryFit.Unknown;
        }

        if (totalMb <= vramMb)
        {
            return MemoryFit.Vram;
        }

        if (totalMb <= vramMb + usableRamMb)
        {
            return MemoryFit.Offload;
        }

        return MemoryFit.TooBig;
    }
}
