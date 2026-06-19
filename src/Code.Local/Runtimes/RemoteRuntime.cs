using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeLocal.Models;
using CodeLocal.Runtimes.Models;

namespace CodeLocal.Runtimes;

/// <summary>
/// Stand-in runtime for configurations that point Copilot at an existing remote
/// (OpenAI-compatible) endpoint rather than a locally managed host. It owns no server and
/// installs nothing, so the launch path can treat every configuration uniformly instead of
/// special-casing "is this a remote endpoint?". Operations that require a local runtime
/// (downloading, preparing, smoke-testing or benchmarking a model) are not supported.
/// </summary>
public sealed class RemoteRuntime : ModelRuntimeBase
{
    /// <summary>
    /// Stable key identifying a remote-endpoint configuration (see <see cref="Key"/>).
    /// </summary>
    public const string RuntimeKey = "remote";

    /// <summary>
    /// The stable key for a remote-endpoint configuration ("remote").
    /// </summary>
    public override string Key => RuntimeKey;

    /// <summary>
    /// Human-readable runtime name shown to the user: "remote endpoint".
    /// </summary>
    public override string Name => "remote endpoint";

    /// <summary>
    /// Always true: there is nothing to install locally for a remote endpoint.
    /// </summary>
    public override bool IsInstalled => true;

    /// <summary>
    /// False: a remote endpoint is an external service, not a locally managed server.
    /// </summary>
    public override bool ManagesLocalServer => false;

    /// <summary>
    /// A remote endpoint has no local installer.
    /// </summary>
    public override IRuntimeInstaller? Installer => null;

    /// <summary>
    /// Not applicable: a remote endpoint's base URL comes from the saved configuration,
    /// not from the runtime.
    /// </summary>
    public override string OpenAiBaseUrl =>
        throw new NotSupportedException(
            "A remote endpoint's base URL is taken from the saved configuration, not the runtime.");

    /// <summary>
    /// Always reports reachable: Code.Local does not manage or probe the remote endpoint.
    /// </summary>
    public override Task<bool> IsServerReachableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// No locally installed models to report for a remote endpoint.
    /// </summary>
    public override Task<IReadOnlyList<InstalledModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<InstalledModel>>(new List<InstalledModel>());
    }

    /// <summary>
    /// Not supported: a remote endpoint serves its own models.
    /// </summary>
    public override Task<int> DownloadAsync(CodingModel model, Action<string> onLine, CancellationToken cancellationToken = default)
    {
        throw NotSupportedForRemote("Downloading a model");
    }

    /// <summary>
    /// Not supported: a remote endpoint serves its own models.
    /// </summary>
    public override Task<string> PrepareAsync(CodingModel model, int numCtx, Action<string> onLine, CancellationToken cancellationToken = default)
    {
        throw NotSupportedForRemote("Preparing a model");
    }

    /// <summary>
    /// Not supported: a remote endpoint serves its own models.
    /// </summary>
    public override Task<string?> SmokeTestAsync(string modelId, string prompt, CancellationToken cancellationToken = default)
    {
        throw NotSupportedForRemote("Smoke-testing a model");
    }

    /// <summary>
    /// Not supported: benchmarking measures a local runtime's generation speed.
    /// </summary>
    public override Task<GenerationMetrics> BenchmarkAsync(string modelId, string prompt, int numPredict, CancellationToken cancellationToken = default)
    {
        throw NotSupportedForRemote("Benchmarking");
    }

    /// <summary>
    /// Builds the exception thrown for operations that require a locally managed runtime.
    /// </summary>
    /// <param name="operation">The human-readable operation that is unavailable for a remote endpoint.</param>
    private static NotSupportedException NotSupportedForRemote(string operation)
    {
        return new NotSupportedException(
            $"{operation} is not supported for a remote endpoint; it requires a local runtime.");
    }
}
