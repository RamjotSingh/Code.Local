using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Models;
using CodeLocal.Runtimes.Models;

namespace CodeLocal.Runtimes.Ollama;

/// <summary>
/// Drives a local Ollama install as a Code.Local model runtime. Resolves catalog models
/// to Ollama tags, pulls weights, pins the context window via a derived model, and
/// exposes the OpenAI-compatible endpoint Copilot should target.
/// </summary>
public sealed class OllamaRuntime : ModelRuntimeBase
{
    /// <summary>
    /// Stable key identifying the Ollama runtime (see <see cref="Key"/>).
    /// </summary>
    public const string RuntimeKey = "ollama";

    private readonly OllamaService _service = new OllamaService();

    public override string Key => RuntimeKey;

    public override string Name => "Ollama";

    public override bool IsInstalled => _service.IsInstalled;

    public override string OpenAiBaseUrl => $"{OllamaService.DefaultHost}/v1";

    public override IRuntimeInstaller? Installer { get; } = new OllamaInstaller();

    /// <summary>
    /// Check if the Ollama server is currently reachable.
    /// </summary>
    public override Task<bool> IsServerReachableAsync(CancellationToken cancellationToken = default)
    {
        return _service.IsServerReachableAsync(cancellationToken);
    }

    /// <summary>
    /// Ensure the Ollama server is running, starting it if needed.
    /// </summary>
    public override async Task<bool> EnsureServerRunningAsync(Action<string> onLine, CancellationToken cancellationToken = default)
    {
        if (await _service.IsServerReachableAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        onLine("Starting the Ollama server...");
        _service.StartServer();

        for (int attemptNumber = 0; attemptNumber < 8; attemptNumber++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken).ConfigureAwait(false);

            if (await _service.IsServerReachableAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// List the models already pulled into Ollama.
    /// </summary>
    public override Task<IReadOnlyList<InstalledModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        return _service.ListModelsAsync(cancellationToken);
    }

    /// <summary>
    /// Download a model by pulling its base tag from Ollama.
    /// </summary>
    public override Task<int> DownloadAsync(CodingModel model, Action<string> onLine, CancellationToken cancellationToken = default)
    {
        string tag = RequireTag(model);
        return _service.PullAsync(tag, cancellationToken);
    }

    /// <summary>
    /// Create a derived model that pins the requested context size while preserving the base
    /// model's template and tool-call renderer/parser, then return its ID.
    /// </summary>
    public override async Task<string> PrepareAsync(
        CodingModel model, int numCtx, Action<string> onLine, CancellationToken cancellationToken = default)
    {
        string tag = RequireTag(model);
        string derivedId = model.Id;

        string baseModelfile = await _service.ShowModelfileAsync(tag, cancellationToken).ConfigureAwait(false);
        string modelfile = OllamaService.BuildModelfile(baseModelfile, numCtx);
        int exitCode = await _service.CreateModelAsync(derivedId, modelfile, cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"`ollama create {derivedId}` failed with exit code {exitCode}.");
        }

        return derivedId;
    }

    /// <summary>
    /// Run a smoke test generation to verify the model responds.
    /// </summary>
    public override Task<string?> SmokeTestAsync(string modelId, string prompt, CancellationToken cancellationToken = default)
    {
        return _service.SmokeTestAsync(modelId, prompt, cancellationToken);
    }

    /// <summary>
    /// Run a benchmark generation and return phase timings.
    /// </summary>
    public override Task<GenerationMetrics> BenchmarkAsync(
        string modelId, string prompt, int numPredict, CancellationToken cancellationToken = default)
    {
        return _service.GenerateWithMetricsAsync(modelId, prompt, numPredict, cancellationToken);
    }

    /// <summary>
    /// Get the GPU/CPU placement of a loaded model.
    /// </summary>
    public override Task<string?> GetPlacementAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return _service.GetPlacementAsync(modelId, cancellationToken);
    }

    /// <summary>
    /// Apply recommended Ollama server environment variables.
    /// </summary>
    public override void ApplyRecommendedSettings(Action<string> log)
    {
        foreach (KeyValuePair<string, string> setting in OllamaService.RecommendedServerEnv)
        {
            TrySetUserEnv(setting.Key, setting.Value, log);
        }

        log("These curb CPU offloading and idle unloading. Code.Local already starts Ollama with them; a server you launched yourself needs a restart to pick them up.");
    }

    /// <summary>
    /// Get the Ollama tag for a model or throw if unsupported.
    /// </summary>
    private string RequireTag(CodingModel model)
    {
        return model.SourceFor(Key)
           ?? throw new NotSupportedException($"{model.DisplayName} has no Ollama source.");
    }

    /// <summary>
    /// Try to set a user-level environment variable (Windows only).
    /// </summary>
    private static void TrySetUserEnv(string variableName, string variableValue, Action<string> log)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Environment.SetEnvironmentVariable(variableName, variableValue, EnvironmentVariableTarget.User);
                log($"Persisted {variableName}={variableValue} (user env).");
            }
            else
            {
                log($"Recommend setting {variableName}={variableValue} in your shell profile and restarting Ollama.");
            }
        }
        catch (Exception exception)
        {
            log($"Could not set {variableName}: {exception.Message}");
        }
    }
}
