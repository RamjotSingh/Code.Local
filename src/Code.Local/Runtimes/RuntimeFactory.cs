using System;
using System.Collections.Generic;
using CodeLocal.Runtimes.Ollama;

namespace CodeLocal.Runtimes;

/// <summary>
/// Resolves a model-host runtime by key. The single place new runtimes are registered.
/// </summary>
public static class RuntimeFactory
{
    /// <summary>
    /// The list of available runtime keys.
    /// </summary>
    public static IReadOnlyList<string> Available { get; } = new[] { OllamaRuntime.RuntimeKey };

    /// <summary>
    /// Resolve a runtime by key. Null/empty selects the default (Ollama).
    /// </summary>
    public static IModelRuntime Create(string? name)
    {
        string runtimeKey = string.IsNullOrWhiteSpace(name) ? OllamaRuntime.RuntimeKey : name.Trim().ToLowerInvariant();
        return runtimeKey switch
        {
            OllamaRuntime.RuntimeKey => new OllamaRuntime(),
            RemoteRuntime.RuntimeKey => new RemoteRuntime(),
            _ => throw new NotSupportedException(
                $"Unknown runtime '{name}'. Available: {string.Join(", ", Available)}.")
        };
    }
}
