using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Models;
using CodeLocal.Runtimes.Models;

namespace CodeLocal.Runtimes;

/// <summary>
/// Base class for model runtimes that supplies the behavior shared by all runtimes
/// (model-support resolution and sensible defaults), leaving host-specific operations
/// abstract. Concrete runtimes inherit this instead of re-implementing the common pieces;
/// the <see cref="IModelRuntime"/> interface stays a pure contract.
/// </summary>
public abstract class ModelRuntimeBase : IModelRuntime
{
    /// <summary>
    /// Stable key used to resolve per-runtime model references and for registration
    /// (e.g. "ollama", "vllm").
    /// </summary>
    public abstract string Key { get; }

    /// <summary>
    /// Human-readable runtime name, e.g. "Ollama".
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// True if the runtime's executable is available on this machine.
    /// </summary>
    public abstract bool IsInstalled { get; }

    /// <summary>
    /// True when Code.Local manages this runtime's local server. Default: true; a runtime that
    /// only points at an external endpoint overrides this to false.
    /// </summary>
    public virtual bool ManagesLocalServer => true;

    /// <summary>
    /// OpenAI-compatible base URL that Copilot should be pointed at.
    /// </summary>
    public abstract string OpenAiBaseUrl { get; }

    /// <summary>
    /// The installer for this runtime, or null if automatic install isn't supported.
    /// </summary>
    public abstract IRuntimeInstaller? Installer { get; }

    /// <summary>
    /// True if the runtime can host the given model (it has a source for this runtime).
    /// </summary>
    public bool Supports(CodingModel model)
    {
        return model.SourceFor(Key) is not null;
    }

    /// <summary>
    /// The runtime-specific model reference for the given model, or null if unsupported.
    /// </summary>
    public string? ModelRef(CodingModel model)
    {
        return model.SourceFor(Key);
    }

    /// <summary>
    /// True if the runtime's server is currently responding.
    /// </summary>
    public abstract Task<bool> IsServerReachableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure the runtime's server is running. Default behavior just reports current
    /// reachability; a runtime that can start its own server overrides this.
    /// </summary>
    public virtual Task<bool> EnsureServerRunningAsync(Action<string> onLine, CancellationToken cancellationToken = default)
    {
        return IsServerReachableAsync(cancellationToken);
    }

    /// <summary>
    /// Models already available on the runtime.
    /// </summary>
    public abstract Task<IReadOnlyList<InstalledModel>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure the base model weights are downloaded. Returns 0 on success. May skip if
    /// already present.
    /// </summary>
    public abstract Task<int> DownloadAsync(CodingModel model, Action<string> onLine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply the requested context size and return the model identifier Copilot should
    /// request (the value for COPILOT_MODEL). Throws on failure.
    /// </summary>
    public abstract Task<string> PrepareAsync(CodingModel model, int numCtx, Action<string> onLine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quick generation call to confirm the model responds. Returns the text, or null.
    /// </summary>
    public abstract Task<string?> SmokeTestAsync(string modelId, string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run one timed generation and return its phase timings (load / prefill / decode), so a
    /// benchmark can isolate steady-state speed from the one-time model load. Throws on failure.
    /// </summary>
    public abstract Task<GenerationMetrics> BenchmarkAsync(string modelId, string prompt, int numPredict, CancellationToken cancellationToken = default);

    /// <summary>
    /// A human-readable GPU/CPU placement for a loaded model, or null. Default: null;
    /// runtimes that can report placement override this.
    /// </summary>
    public virtual Task<string?> GetPlacementAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Apply runtime-specific performance settings (best effort). Default: no-op; runtimes
    /// with tunable settings override this.
    /// </summary>
    public virtual void ApplyRecommendedSettings(Action<string> log)
    {
    }
}
