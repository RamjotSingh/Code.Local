using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CodeLocal.Services.Models;
using Microsoft.Win32;

namespace CodeLocal.Services;

/// <summary>
/// Puts the running codelocal binary's folder on the user PATH so `codelocal` works from any
/// shell, keeping a single managed entry — a folder it previously added is replaced rather
/// than accumulated. Windows edits the user PATH variable directly; Unix writes a small env
/// file that the shell profiles source.
/// </summary>
public static class BinaryPathRegistrar
{
    private const string UnixMarker = "# codelocal PATH";

    /// <summary>
    /// The folder containing the running codelocal executable, or null when it can't be resolved.
    /// </summary>
    public static string? BinaryDirectoryOrNull()
    {
        string? processPath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(processPath))
        {
            return null;
        }

        return Path.GetDirectoryName(processPath);
    }

    /// <summary>
    /// Ensure the binary's folder is the single codelocal entry on the user PATH, replacing a
    /// folder it previously added. Returns the outcome for the caller to report.
    /// </summary>
    public static PathRegistrationState Register()
    {
        string? binaryDirectory = BinaryDirectoryOrNull();

        if (string.IsNullOrEmpty(binaryDirectory))
        {
            return PathRegistrationState.Failed;
        }

        if (OperatingSystem.IsWindows())
        {
            return RegisterWindows(binaryDirectory);
        }

        return RegisterUnix(binaryDirectory);
    }

    /// <summary>
    /// Add the folder to the Windows user PATH, dropping any previously-registered folder and
    /// duplicates, then placing it first so it wins over a stale codelocal elsewhere. The value
    /// is read/written through the registry so a REG_EXPAND_SZ PATH keeps its %VARS% intact.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static PathRegistrationState RegisterWindows(string binaryDirectory)
    {
        using RegistryKey environmentKey = Registry.CurrentUser.OpenSubKey("Environment", writable: true)
            ?? Registry.CurrentUser.CreateSubKey("Environment");

        object? existingValue = environmentKey.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        string currentPath = existingValue as string ?? string.Empty;
        RegistryValueKind valueKind = existingValue is null
            ? RegistryValueKind.ExpandString
            : environmentKey.GetValueKind("Path");

        List<string> entries = SplitPathEntries(currentPath);
        string? previousDirectory = ReadRegisteredDirectoryOrNull();

        bool alreadyPresent = entries.Exists(entry => PathsEqual(entry, binaryDirectory));
        bool hasStaleEntry = previousDirectory is not null
            && !PathsEqual(previousDirectory, binaryDirectory)
            && entries.Exists(entry => PathsEqual(entry, previousDirectory));

        if (alreadyPresent && !hasStaleEntry)
        {
            WriteRegisteredDirectory(binaryDirectory);

            return PathRegistrationState.AlreadyOnPath;
        }

        entries.RemoveAll(entry =>
            PathsEqual(entry, binaryDirectory)
            || (previousDirectory is not null && PathsEqual(entry, previousDirectory)));
        entries.Insert(0, binaryDirectory);

        environmentKey.SetValue("Path", string.Join(Path.PathSeparator, entries), valueKind);
        BroadcastEnvironmentChange();
        WriteRegisteredDirectory(binaryDirectory);

        return hasStaleEntry ? PathRegistrationState.UpdatedOnPath : PathRegistrationState.AddedToPath;
    }

    /// <summary>
    /// Notify the shell (Explorer and other listeners) that the environment changed, so a
    /// terminal opened afterwards inherits the updated PATH without a sign-out.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void BroadcastEnvironmentChange()
    {
        SendMessageTimeout((IntPtr)HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, 5000, out _);
    }

    private const int HwndBroadcast = 0xFFFF;
    private const int WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle, int message, IntPtr wParam, string lParam, uint flags, uint timeoutMilliseconds, out IntPtr result);

    /// <summary>
    /// Add the folder to the Unix user PATH by writing a sourced env file and referencing it
    /// from the shell profiles. The file always exports just this folder, so a folder it
    /// previously pointed at is replaced rather than accumulated.
    /// </summary>
    private static PathRegistrationState RegisterUnix(string binaryDirectory)
    {
        string configDirectory = ConfigStore.ConfigDirectory();
        Directory.CreateDirectory(configDirectory);
        string envFile = Path.Combine(configDirectory, "path.env.sh");

        string desiredContent = $"{UnixMarker}\nexport PATH=\"{binaryDirectory}:$PATH\"\n";
        string? previousContent = File.Exists(envFile) ? File.ReadAllText(envFile) : null;
        File.WriteAllText(envFile, desiredContent);

        string sourceLine = $"[ -f \"{envFile}\" ] && . \"{envFile}\"  {UnixMarker}";
        string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        bool addedSourceLine = false;

        foreach (string profileName in new[] { ".zshrc", ".bashrc", ".profile" })
        {
            string profilePath = Path.Combine(homeDirectory, profileName);

            if (!File.Exists(profilePath))
            {
                continue;
            }

            if (!File.ReadAllText(profilePath).Contains(UnixMarker))
            {
                File.AppendAllText(profilePath, Environment.NewLine + sourceLine + Environment.NewLine);
                addedSourceLine = true;
            }
        }

        if (addedSourceLine)
        {
            return PathRegistrationState.AddedToPath;
        }

        if (previousContent == desiredContent)
        {
            return PathRegistrationState.AlreadyOnPath;
        }

        return PathRegistrationState.UpdatedOnPath;
    }

    /// <summary>
    /// Split a PATH value into its individual folder entries, dropping empties and whitespace.
    /// </summary>
    private static List<string> SplitPathEntries(string path)
    {
        string[] parts = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new List<string>(parts);
    }

    /// <summary>
    /// Compare two folder paths for equality, ignoring case and a trailing separator (Windows).
    /// </summary>
    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            left.TrimEnd('\\', '/'),
            right.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The file recording the folder codelocal last added to PATH, used to remove it on a move.
    /// </summary>
    private static string RegisteredDirectoryFile()
    {
        return Path.Combine(ConfigStore.ConfigDirectory(), "path-entry");
    }

    /// <summary>
    /// Read the folder codelocal previously added to PATH, or null when none is recorded.
    /// </summary>
    private static string? ReadRegisteredDirectoryOrNull()
    {
        string stateFile = RegisteredDirectoryFile();

        if (!File.Exists(stateFile))
        {
            return null;
        }

        string recorded = File.ReadAllText(stateFile).Trim();

        return recorded.Length == 0 ? null : recorded;
    }

    /// <summary>
    /// Record the folder codelocal added to PATH so a later move can replace it.
    /// </summary>
    private static void WriteRegisteredDirectory(string directory)
    {
        Directory.CreateDirectory(ConfigStore.ConfigDirectory());
        File.WriteAllText(RegisteredDirectoryFile(), directory);
    }
}
