using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Runtimes.Models;
using CodeLocal.Runtimes.Ollama.Models;
using CodeLocal.Services;

namespace CodeLocal.Runtimes.Ollama;

/// <summary>
/// Low-level operations against a local Ollama install: process invocations
/// (pull/create) and HTTP calls (tags/generate). runtime-agnostic callers should go
/// through <see cref="OllamaRuntime"/> rather than this class directly.
/// </summary>
public sealed class OllamaService
{
    public const string DefaultHost = "http://localhost:11434";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Server-level Ollama settings that curb aggressive CPU offloading and idle
    /// unloading for a single-user coding setup. NUM_PARALLEL=1 stops Ollama reserving KV
    /// cache for up to 4 concurrent slots (the main reason layers spill to CPU);
    /// KEEP_ALIVE=-1 keeps the model resident instead of unloading after ~5 min; flash
    /// attention + q8 KV shrink the cache further. Read by the server at startup.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RecommendedServerEnv =
        new Dictionary<string, string>
        {
            ["OLLAMA_NUM_PARALLEL"] = "1",
            ["OLLAMA_KEEP_ALIVE"] = "-1",
            ["OLLAMA_FLASH_ATTENTION"] = "1",
            ["OLLAMA_KV_CACHE_TYPE"] = "q8_0",
        };

    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    /// <summary>
    /// Full path to the ollama executable, or null if it can't be found.
    /// </summary>
    public string? ExecutablePath
    {
        get
        {
            string? executablePath = ProcessRunner.GetFullPathForExecutableOrNull("ollama");

            if (executablePath is not null)
            {
                return executablePath;
            }

            foreach (string candidatePath in KnownExecutableLocations())
            {
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// True if the ollama executable is present on this machine.
    /// </summary>
    public bool IsInstalled => ExecutablePath is not null;

    /// <summary>
    /// True if the Ollama server answers on its default port.
    /// </summary>
    public async Task<bool> IsServerReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(2));
            using HttpResponseMessage response = await Http.GetAsync(DefaultHost, timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Start the Ollama server in the background (best effort). Healthy installs auto-start
    /// it; this nudges a stopped one. A broken/orphaned binary will fail to bind, which the
    /// caller detects by polling reachability.
    /// </summary>
    public void StartServer()
    {
        string? executablePath = ExecutablePath;

        if (executablePath is null)
        {
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("serve");
            // Start the server with our single-user settings (unless the user has set
            // their own), so models stay on the GPU without a manual restart.
            foreach (KeyValuePair<string, string> setting in RecommendedServerEnv)
            {
                if (!startInfo.Environment.ContainsKey(setting.Key))
                {
                    startInfo.Environment[setting.Key] = setting.Value;
                }
            }
            Process.Start(startInfo);
        }
        catch
        {
            // best effort; EnsureServerRunningAsync surfaces a clear error if it never comes up
        }
    }

    /// <summary>
    /// List the models already pulled into Ollama.
    /// </summary>
    public async Task<IReadOnlyList<InstalledModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            OllamaTagsResponse? tagsResponse = await Http
                .GetFromJsonAsync<OllamaTagsResponse>($"{DefaultHost}/api/tags", JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (tagsResponse is null)
            {
                return Array.Empty<InstalledModel>();
            }

            List<InstalledModel> installedModels = new List<InstalledModel>(tagsResponse.Models.Count);

            foreach (OllamaModelEntry modelEntry in tagsResponse.Models)
            {
                installedModels.Add(new InstalledModel { Name = modelEntry.Name, SizeBytes = modelEntry.Size });
            }

            return installedModels;
        }
        catch
        {
            return Array.Empty<InstalledModel>();
        }
    }

    /// <summary>
    /// True if a model with the given tag is already pulled.
    /// </summary>
    public async Task<bool> HasModelAsync(string tag, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InstalledModel> installedModels = await ListModelsAsync(cancellationToken).ConfigureAwait(false);

        foreach (InstalledModel installedModel in installedModels)
        {
            if (string.Equals(installedModel.Name, tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Pull a model by tag via the ollama CLI. The CLI owns the terminal so its native
    /// download progress renders correctly.
    /// </summary>
    public async Task<int> PullAsync(string tag, CancellationToken cancellationToken = default)
    {
        string executablePath = RequireExecutable();
        return await ProcessRunner
            .RunExecutableWithoutOutputCapturedAsync(executablePath, new[] { "pull", tag }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Create a derived model from a Modelfile via the ollama CLI (it renders its own
    /// progress on the inherited terminal).
    /// </summary>
    public async Task<int> CreateModelAsync(
        string name, string modelfile, CancellationToken cancellationToken = default)
    {
        string executablePath = RequireExecutable();
        string modelfilePath = Path.Combine(Path.GetTempPath(), $"codelocal-{Guid.NewGuid():N}.Modelfile");
        await File.WriteAllTextAsync(modelfilePath, modelfile, cancellationToken).ConfigureAwait(false);

        try
        {
            return await ProcessRunner
                .RunExecutableWithoutOutputCapturedAsync(executablePath, new[] { "create", name, "-f", modelfilePath }, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(modelfilePath);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    /// <summary>
    /// Run a single non-streaming generation to confirm the model responds.
    /// </summary>
    public async Task<string?> SmokeTestAsync(string modelId, string prompt, CancellationToken cancellationToken = default)
    {
        OllamaGenerateRequest request = new OllamaGenerateRequest
        {
            Model = modelId,
            Prompt = prompt,
            Stream = false,
        };
        using HttpResponseMessage response = await Http
            .PostAsJsonAsync($"{DefaultHost}/api/generate", request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        OllamaGenerateResponse? responseBody = await response.Content
            .ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return responseBody?.Response;
    }

    /// <summary>
    /// Run one timed, deterministic generation and return its phase timings. Uses Ollama's
    /// native timing fields (which separate load, prefill, and decode) and pins the model
    /// resident (keep_alive -1) so warmup and measured runs share the same loaded weights.
    /// </summary>
    public async Task<GenerationMetrics> GenerateWithMetricsAsync(
        string modelId, string prompt, int numPredict, CancellationToken cancellationToken = default)
    {
        OllamaGenerateRequest request = new OllamaGenerateRequest
        {
            Model = modelId,
            Prompt = prompt,
            Stream = false,
            KeepAlive = -1,
            Options = new OllamaGenerateOptions { NumPredict = numPredict, Temperature = 0 },
        };
        using HttpResponseMessage response = await Http
            .PostAsJsonAsync($"{DefaultHost}/api/generate", request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        OllamaGenerateResponse responseBody = await response.Content
            .ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Ollama returned an empty response to the benchmark request.");

        const double nsPerSecond = 1_000_000_000.0;
        return new GenerationMetrics
        {
            PromptTokens = responseBody.PromptEvalCount,
            PromptSeconds = responseBody.PromptEvalDuration / nsPerSecond,
            GeneratedTokens = responseBody.EvalCount,
            GenerateSeconds = responseBody.EvalDuration / nsPerSecond,
            LoadSeconds = responseBody.LoadDuration / nsPerSecond,
        };
    }

    /// <summary>
    /// Report how a loaded model is split between GPU and CPU (from GET /api/ps), or null if
    /// it isn't currently loaded or Ollama can't be reached.
    /// </summary>
    public async Task<string?> GetPlacementAsync(string modelId, CancellationToken cancellationToken = default)
    {
        try
        {
            OllamaPsResponse? processResponse = await Http
                .GetFromJsonAsync<OllamaPsResponse>($"{DefaultHost}/api/ps", JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (processResponse is null)
            {
                return null;
            }

            foreach (OllamaPsModel loadedModel in processResponse.Models)
            {
                if (!Matches(loadedModel.Name, modelId) && !Matches(loadedModel.Model, modelId))
                {
                    continue;
                }

                if (loadedModel.Size <= 0)
                {
                    return null;
                }

                int gpuPercentage = Math.Clamp((int)Math.Round(100.0 * loadedModel.SizeVram / loadedModel.Size), 0, 100);

                return gpuPercentage >= 99 ? "100% GPU (resident)"
                    : gpuPercentage <= 1 ? "100% CPU"
                    : $"{gpuPercentage}% GPU / {100 - gpuPercentage}% CPU";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if a process name matches the model ID.
    /// </summary>
    private static bool Matches(string processName, string modelId)
    {
        return string.Equals(processName, modelId, StringComparison.OrdinalIgnoreCase)
           || processName.StartsWith(modelId + ":", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Capture a model's full Modelfile via `ollama show <tag> --modelfile`, including its
    /// TEMPLATE, RENDERER, PARSER and default parameters, so a derived model can preserve them.
    /// </summary>
    public async Task<string> ShowModelfileAsync(string tag, CancellationToken cancellationToken = default)
    {
        string executablePath = RequireExecutable();
        (int exitCode, string standardOutput, string standardError) = await ProcessRunner
            .RunExecutableWithOutputCapturedAsync(executablePath, new[] { "show", tag, "--modelfile" }, cancellationToken)
            .ConfigureAwait(false);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(standardOutput))
        {
            throw new InvalidOperationException(
                $"`ollama show {tag} --modelfile` failed (exit {exitCode}). {standardError}".Trim());
        }

        return standardOutput;
    }

    /// <summary>
    /// Build a derived Modelfile from a base model's full Modelfile (as emitted by
    /// `ollama show --modelfile`), overriding the context window and neutralizing
    /// `presence_penalty`. Deriving from the full Modelfile keeps the base TEMPLATE / RENDERER
    /// / PARSER — without which tool calling silently breaks for models with a custom renderer
    /// (Qwen3.5 / Qwen3-Coder; see ollama/ollama#12792). `presence_penalty 0` overrides Qwen's
    /// aggressive default (1.5) that stops the model reproducing file content verbatim — the
    /// cause of Copilot "Edit: No match found" loops; it is a no-op for models that don't set it.
    /// </summary>
    public static string BuildModelfile(string baseModelfile, int numCtx)
    {
        string trimmedBase = baseModelfile.Replace("\r\n", "\n").TrimEnd('\n', ' ', '\t');

        return $"{trimmedBase}\nPARAMETER num_ctx {numCtx}\nPARAMETER presence_penalty 0\n";
    }

    /// <summary>
    /// Require the executable path or throw.
    /// </summary>
    private string RequireExecutable()
    {
        return ExecutablePath ?? throw new InvalidOperationException("Ollama executable not found on PATH.");
    }

    /// <summary>
    /// Well-known install locations to probe when ollama isn't on PATH (e.g. a fresh
    /// install in the current shell hasn't picked up the updated PATH yet).
    /// </summary>
    private static List<string> KnownExecutableLocations()
    {
        List<string> locationPaths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            locationPaths.Add(Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe"));
            locationPaths.Add(Path.Combine(programFiles, "Ollama", "ollama.exe"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            locationPaths.Add("/usr/local/bin/ollama");
            locationPaths.Add("/opt/homebrew/bin/ollama");
            locationPaths.Add("/Applications/Ollama.app/Contents/Resources/ollama");
        }
        else
        {
            locationPaths.Add("/usr/local/bin/ollama");
            locationPaths.Add("/usr/bin/ollama");
        }

        return locationPaths;
    }
}
