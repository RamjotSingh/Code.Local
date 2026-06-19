namespace CodeLocal.Services.Models;

/// <summary>
/// The outcome of registering the codelocal binary's folder on the user PATH.
/// </summary>
public enum PathRegistrationState
{
    /// <summary>
    /// The PATH couldn't be updated (e.g. the binary's location couldn't be determined).
    /// </summary>
    Failed,

    /// <summary>
    /// The folder was newly added to the PATH.
    /// </summary>
    AddedToPath,

    /// <summary>
    /// A folder codelocal previously added was replaced with the current one (the binary moved).
    /// </summary>
    UpdatedOnPath,

    /// <summary>
    /// The folder was already the registered PATH entry; nothing changed.
    /// </summary>
    AlreadyOnPath,
}
