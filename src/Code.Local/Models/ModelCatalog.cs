using System;
using System.Collections.Generic;

namespace CodeLocal.Models;

/// <summary>
/// The built-in set of coding models Code.Local can install, spanning hardware tiers
/// from ~3 GB to ~300 GB. Every entry is a tool-calling-capable instruct model, which
/// Copilot's agent loop requires. Per-runtime references live in each model's Sources.
/// </summary>
public static class ModelCatalog
{
    /// <summary>
    /// Build the per-runtime source map for a model hosted on Ollama under the given tag.
    /// </summary>
    /// <param name="tag">The Ollama model tag (for example "qwen3.5:4b").</param>
    private static IReadOnlyDictionary<string, string> Ollama(string tag)
    {
        return new Dictionary<string, string> { ["ollama"] = tag };
    }

    public static readonly CodingModel Qwen35_2B = new CodingModel
    {
        Id = "qwen3.5-2b",
        DisplayName = "Qwen3.5 2B",
        ParamSize = "2B",
        Quantization = "Q8",
        MinVramMb = 3500,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 114688,
        ApproxDiskGb = 2.7,
        License = "Apache-2.0",
        Description = "Qwen3.5 (newer generation) with tool calling; ultra-light for small GPUs. 256K-capable.",
        Sources = Ollama("qwen3.5:2b"),
    };

    public static readonly CodingModel Qwen35_2B_Q4 = new CodingModel
    {
        Id = "qwen3.5-2b-q4",
        DisplayName = "Qwen3.5 2B",
        ParamSize = "2B",
        Quantization = "Q4_K_M",
        MinVramMb = 3000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 114688,
        ApproxDiskGb = 1.9,
        License = "Apache-2.0",
        Description = "Smallest footprint (Q4) for ~4 GB GPUs. Weakest option - use only if the Q8 2B or a 4B won't fit.",
        Sources = Ollama("qwen3.5:2b-q4_K_M"),
    };

    public static readonly CodingModel Qwen35_4B = new CodingModel
    {
        Id = "qwen3.5-4b",
        DisplayName = "Qwen3.5 4B",
        ParamSize = "4B",
        Quantization = "Q4_K_M",
        MinVramMb = 5000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 147456,
        ApproxDiskGb = 3.4,
        License = "Apache-2.0",
        Description = "Qwen3.5 with tool calling; fits ~6 GB GPUs. Newer gen than Qwen2.5-Coder at this size. A Q8 variant is also offered for higher tool-calling fidelity.",
        Sources = Ollama("qwen3.5:4b"),
    };

    public static readonly CodingModel Qwen35_4B_Q8 = new CodingModel
    {
        Id = "qwen3.5-4b-q8",
        DisplayName = "Qwen3.5 4B",
        ParamSize = "4B",
        Quantization = "Q8",
        MinVramMb = 6800,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 147456,
        ApproxDiskGb = 5.3,
        License = "Apache-2.0",
        Description = "Qwen3.5 4B at Q8: near-FP16 fidelity for more reliable agentic tool calling. Prefer over the Q4 4B when it fits (~7 GB).",
        Sources = Ollama("qwen3.5:4b-q8_0"),
    };

    public static readonly CodingModel Qwen35_9B = new CodingModel
    {
        Id = "qwen3.5-9b",
        DisplayName = "Qwen3.5 9B",
        ParamSize = "9B",
        Quantization = "Q4_K_M",
        MinVramMb = 8500,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 163840,
        ApproxDiskGb = 6.6,
        License = "Apache-2.0",
        Description = "Strongest sub-10B Qwen3.5; tool calling + reasoning. Needs ~8 GB VRAM. A Q8 variant is also offered.",
        Sources = Ollama("qwen3.5:9b"),
    };

    public static readonly CodingModel Qwen35_9B_Q8 = new CodingModel
    {
        Id = "qwen3.5-9b-q8",
        DisplayName = "Qwen3.5 9B",
        ParamSize = "9B",
        Quantization = "Q8",
        MinVramMb = 13000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 163840,
        ApproxDiskGb = 11.0,
        License = "Apache-2.0",
        Description = "Qwen3.5 9B at Q8: highest single-step/tool-calling fidelity sub-12B. Needs ~13 GB; a bigger Q4 model may be smarter if you have the VRAM.",
        Sources = Ollama("qwen3.5:9b-q8_0"),
    };

    public static readonly CodingModel Gemma4_12B = new CodingModel
    {
        Id = "gemma4-12b",
        DisplayName = "Gemma 4 12B",
        ParamSize = "12B",
        Quantization = "QAT int4",
        MinVramMb = 9500,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 160000,
        ApproxDiskGb = 7.2,
        License = "Gemma",
        Description = "Google Gemma 4 (native tools, multimodal, thinking). Google's QAT int4 build: near-bf16 quality at ~Q4 size, so it beats plain Q4_K_M. Fills the ~10 GB tier.",
        Sources = Ollama("gemma4:12b-it-qat"),
    };

    public static readonly CodingModel Gemma4_12B_Q8 = new CodingModel
    {
        Id = "gemma4-12b-q8",
        DisplayName = "Gemma 4 12B",
        ParamSize = "12B",
        Quantization = "Q8",
        MinVramMb = 15500,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 160000,
        ApproxDiskGb = 13.0,
        License = "Gemma",
        Description = "Gemma 4 12B at Q8: maximum fidelity for ~16 GB GPUs (the QAT 12B is nearly as good at half the footprint).",
        Sources = Ollama("gemma4:12b-it-q8_0"),
    };

    public static readonly CodingModel GptOss20B = new CodingModel
    {
        Id = "gpt-oss-20b",
        DisplayName = "GPT-OSS 20B",
        ParamSize = "20B MoE",
        Quantization = "MXFP4",
        MinVramMb = 14000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 49152,
        ApproxDiskGb = 14.0,
        License = "Apache-2.0",
        Description = "OpenAI's open model: strong reasoning + tool calling. Needs ~16 GB VRAM.",
        Sources = Ollama("gpt-oss:20b"),
    };

    public static readonly CodingModel Devstral24B = new CodingModel
    {
        Id = "devstral-24b",
        DisplayName = "Devstral Small 24B",
        ParamSize = "24B",
        Quantization = "Q4_K_M",
        MinVramMb = 16000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 163840,
        ApproxDiskGb = 14.0,
        License = "Apache-2.0",
        Description = "Mistral's agentic coding model, tuned specifically for tool-driven agents.",
        Sources = Ollama("devstral:24b"),
    };

    public static readonly CodingModel Qwen3Coder30B = new CodingModel
    {
        Id = "qwen3-coder-30b",
        DisplayName = "Qwen3-Coder 30B",
        ParamSize = "30B MoE",
        Quantization = "Q4_K_M",
        MinVramMb = 18000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 98304,
        ApproxDiskGb = 19.0,
        License = "Apache-2.0",
        Description = "Top agentic coder (3B active MoE) with very long context. Needs ~20+ GB VRAM.",
        Sources = Ollama("qwen3-coder:30b"),
    };

    public static readonly CodingModel Qwen35_27B = new CodingModel
    {
        Id = "qwen3.5-27b",
        DisplayName = "Qwen3.5 27B",
        ParamSize = "27B",
        Quantization = "int4",
        MinVramMb = 20000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 245760,
        ApproxDiskGb = 16.0,
        License = "Apache-2.0",
        Description = "Qwen3.5 27B at int4 (text-only): a bigger model at Q4 for ~20 GB GPUs.",
        Sources = Ollama("qwen3.5:27b-int4"),
    };

    public static readonly CodingModel Gemma4_26B = new CodingModel
    {
        Id = "gemma4-26b",
        DisplayName = "Gemma 4 26B-A4B",
        ParamSize = "26B MoE",
        Quantization = "Q4_K_M",
        MinVramMb = 22000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 200000,
        ApproxDiskGb = 18.0,
        License = "Gemma",
        Description = "Google Gemma 4 MoE (4B active, so fast); native tools. Efficient ~22 GB mid-tier alternative.",
        Sources = Ollama("gemma4:26b"),
    };

    public static readonly CodingModel Qwen35_35B = new CodingModel
    {
        Id = "qwen3.5-35b",
        DisplayName = "Qwen3.5 35B-A3B",
        ParamSize = "35B MoE",
        Quantization = "int4",
        MinVramMb = 24000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 98304,
        ApproxDiskGb = 20.0,
        License = "Apache-2.0",
        Description = "Qwen3.5 35B-A3B MoE (int4, text-only): 35B quality, ~3B active so it stays fast. ~24 GB.",
        Sources = Ollama("qwen3.5:35b-a3b-int4"),
    };

    public static readonly CodingModel GptOss120B = new CodingModel
    {
        Id = "gpt-oss-120b",
        DisplayName = "GPT-OSS 120B",
        ParamSize = "120B MoE",
        Quantization = "MXFP4",
        MinVramMb = 80000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 73728,
        ApproxDiskGb = 65.0,
        License = "Apache-2.0",
        Description = "OpenAI's large open model: frontier reasoning + tools. Needs ~80 GB VRAM/unified memory.",
        Sources = Ollama("gpt-oss:120b"),
    };

    public static readonly CodingModel Qwen3Coder480B = new CodingModel
    {
        Id = "qwen3-coder-480b",
        DisplayName = "Qwen3-Coder 480B",
        ParamSize = "480B MoE",
        Quantization = "Q4_K_M",
        MinVramMb = 300000,
        RecommendedNumCtx = 32768,
        KvBytesPerTokenFp16 = 253952,
        ApproxDiskGb = 290.0,
        License = "Apache-2.0",
        Description = "Frontier agentic coder (35B active MoE), 256K context; 512 GB-class rigs only.",
        Sources = Ollama("qwen3-coder:480b"),
    };

    /// <summary>
    /// All models, ordered from smallest to largest VRAM requirement.
    /// </summary>
    public static readonly IReadOnlyList<CodingModel> All = new[]
    {
        Qwen35_2B_Q4,
        Qwen35_2B,
        Qwen35_4B,
        Qwen35_4B_Q8,
        Qwen35_9B,
        Gemma4_12B,
        Qwen35_9B_Q8,
        GptOss20B,
        Gemma4_12B_Q8,
        Devstral24B,
        Qwen3Coder30B,
        Qwen35_27B,
        Gemma4_26B,
        Qwen35_35B,
        GptOss120B,
        Qwen3Coder480B,
    };

    /// <summary>
    /// Safe default when VRAM can't be detected: the best all-around small model.
    /// </summary>
    public static readonly CodingModel Default = Qwen35_4B;

    /// <summary>
    /// Pick the highest-quality model that fits the detected VRAM, considering all models.
    /// </summary>
    /// <param name="vramMb">Detected/available VRAM in MB, or null if unknown.</param>
    public static CodingModel Recommend(int? vramMb)
    {
        return Recommend(vramMb, All);
    }

    /// <summary>
    /// Pick the highest-quality candidate that fits the detected VRAM. Falls back to the
    /// smallest candidate when VRAM is below every entry, or the default when unknown.
    /// Candidates are expected to be ordered smallest-to-largest.
    /// </summary>
    /// <param name="vramMb">Detected/available VRAM in MB, or null if unknown.</param>
    /// <param name="candidates">Models to choose from, ordered smallest-to-largest VRAM.</param>
    public static CodingModel Recommend(int? vramMb, IReadOnlyList<CodingModel> candidates)
    {
        if (candidates.Count == 0)
        {
            return Default;
        }

        if (vramMb is not int availableVram)
        {
            foreach (CodingModel candidateModel in candidates)
            {
                if (candidateModel == Default)
                {
                    return Default;
                }
            }

            return candidates[0];
        }

        CodingModel? bestMatch = null;

        foreach (CodingModel candidateModel in candidates)
        {
            if (candidateModel.MinVramMb <= availableVram && (bestMatch is null || candidateModel.MinVramMb > bestMatch.MinVramMb))
            {
                bestMatch = candidateModel;
            }
        }

        return bestMatch ?? candidates[0];
    }

    /// <summary>
    /// Find a catalog model by its id or by any of its per-runtime source references
    /// (case-insensitive), or null when nothing matches.
    /// </summary>
    /// <param name="value">A model id (e.g. "qwen3.5-4b") or source tag (e.g. "qwen3.5:4b").</param>
    public static CodingModel? FindByTagOrId(string value)
    {
        foreach (CodingModel catalogModel in All)
        {
            if (string.Equals(catalogModel.Id, value, StringComparison.OrdinalIgnoreCase))
            {
                return catalogModel;
            }

            foreach (string sourceTag in catalogModel.Sources.Values)
            {
                if (string.Equals(sourceTag, value, StringComparison.OrdinalIgnoreCase))
                {
                    return catalogModel;
                }
            }
        }

        return null;
    }
}
