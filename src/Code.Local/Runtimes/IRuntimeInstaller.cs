using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeLocal.Runtimes;

/// <summary>
/// Installs a runtime (and any prerequisites it can manage) on the local machine — a
/// model runtime or the Copilot CLI. Each runtime that supports automatic installation
/// provides one; callers must obtain user consent before invoking it.
/// </summary>
public interface IRuntimeInstaller
{
    /// <summary>
    /// Instructions for installing the runtime manually, shown when automatic install
    /// is declined or unavailable.
    /// </summary>
    string ManualInstallHint { get; }

    /// <summary>
    /// Attempt to install the runtime (best effort). Returns true on success.
    /// </summary>
    Task<bool> InstallAsync(Action<string> onLine, CancellationToken cancellationToken = default);
}
