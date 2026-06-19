using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Models;
using CodeLocal.Runtimes.Models;

namespace CodeLocal.Runtimes;

/// <summary>
/// A local model host that Code.Local can drive (Ollama today; vLLM / llama.cpp later).
/// Implementations own the host-specific details so the commands stay generic.
/// </summary>
public interface IModelRuntime
{
    /// <summary>
    /// Stable key used to resolve per-runtime model references and for registration
    /// (e.g. "ollama", "vllm").
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Human-readable runtime name, e.g. "Ollama".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// True if the runtime's executable is available on this machine.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// True when Code.Local installs and runs this runtime's server locally, so install and
    /// reachability checks are meaningful; false for an external endpoint it only points at.
    /// </summary>
    bool ManagesLocalServer { get; }

    /// <summary>
    /// OpenAI-compatible base URL that Copilot should be pointed at.
    /// </summary>
    string OpenAiBaseUrl { get; }

    /// <summary>
    /// The installer for this runtime, or null if automatic install isn't supported.
    /// </summary>
    IRuntimeInstaller? Installer { get; }

    /// <summary>
    /// True if the runtime can host the given model (it has a source for this runtime).
    /// </summary>
    bool Supports(CodingModel model);

    /// <summary>
    /// The runtime-specific model reference for the given model (e.g. an Ollama tag or a
    /// Hugging Face repo), or null if unsupported.
    /// </summary>
    string? ModelRef(CodingModel model);

    /// <summary>
    /// True if the runtime's server is currently responding.
    /// </summary>
    Task<bool> IsServerReachableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure the runtime's server is running — starting it if the runtime can — and
    /// return true once it's reachable.
    /// </summary>
    Task<bool> EnsureServerRunningAsync(Action<string> onLine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Models already available on the runtime.
    /// </summary>
    Task<IReadOnlyList<InstalledModel>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure the base model weights are downloaded. Returns 0 on success. May skip if
    /// already present.
    /// </summary>
    Task<int> DownloadAsync(CodingModel model, Action<string> onLine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply the requested context size and return the model identifier Copilot should
    /// request (the value for COPILOT_MODEL). Throws on failure.
    /// </summary>
    Task<string> PrepareAsync(CodingModel model, int numCtx, Action<string> onLine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quick generation call to confirm the model responds. Returns the text, or null.
    /// </summary>
    Task<string?> SmokeTestAsync(string modelId, string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run one timed generation and return its phase timings (load / prefill / decode), so a
    /// benchmark can isolate steady-state speed from the one-time model load. Throws on failure.
    /// </summary>
    Task<GenerationMetrics> BenchmarkAsync(string modelId, string prompt, int numPredict, CancellationToken cancellationToken = default);

    /// <summary>
    /// A human-readable GPU/CPU placement for a loaded model (e.g. "100% GPU (resident)" or
    /// "35% GPU / 65% CPU"), or null if the runtime can't report it.
    /// </summary>
    Task<string?> GetPlacementAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply runtime-specific performance settings (best effort).
    /// </summary>
    void ApplyRecommendedSettings(Action<string> log);
}
