using System;
using System.IO;
using System.Text.Json;
using CodeLocal.Models;

namespace CodeLocal.Services;

/// <summary>
/// Reads and writes the saved Code.Local provider configuration as a JSON file under the
/// user's config directory (Windows %APPDATA%\codelocal, Unix ~/.config/codelocal).
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    /// <summary>
    /// Get the configuration directory path.
    /// </summary>
    public static string ConfigDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "codelocal");

    /// <summary>
    /// Get the configuration file path.
    /// </summary>
    public static string ConfigPath()
        => Path.Combine(ConfigDirectory(), "config.json");

    /// <summary>
    /// Save the configuration to the config file.
    /// </summary>
    public static void Save(CodeLocalConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory());
        string configJson = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath(), configJson);
    }

    /// <summary>
    /// Load the saved configuration, or null when none exists. Throws when the file is
    /// present but invalid or incomplete (e.g. missing the required runtime key) rather than
    /// silently treating it as unconfigured.
    /// </summary>
    public static CodeLocalConfig? LoadOrNull()
    {
        string configPath = ConfigPath();

        if (!File.Exists(configPath))
        {
            return null;
        }

        string configJson = File.ReadAllText(configPath);

        CodeLocalConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<CodeLocalConfig>(configJson, JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"The saved Code.Local config at '{configPath}' is invalid or incomplete. Re-run `codelocal init` to recreate it.");
        }

        if (config is null)
        {
            throw new InvalidOperationException(
                $"The saved Code.Local config at '{configPath}' is empty. Re-run `codelocal init` to recreate it.");
        }

        return config;
    }
}
