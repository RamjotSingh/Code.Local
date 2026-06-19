using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodeLocal.Models;
using CodeLocal.Runtimes;

namespace CodeLocal.Copilot;

/// <summary>
/// Bridges a <see cref="CodeLocalConfig"/> to the GitHub Copilot CLI: projects it into the
/// COPILOT_* environment variables Copilot reads, applies them to the user's environment,
/// and reads them back for display. This is the single owner of how Copilot receives config.
/// </summary>
public static class CopilotEnvironment
{
    public const string EnvBaseUrl = "COPILOT_PROVIDER_BASE_URL";
    public const string EnvApiKey = "COPILOT_PROVIDER_API_KEY";
    public const string EnvModel = "COPILOT_MODEL";
    public const string EnvWireApi = "COPILOT_PROVIDER_WIRE_API";
    public const string EnvMaxPromptTokens = "COPILOT_PROVIDER_MAX_PROMPT_TOKENS";
    public const string EnvMaxOutputTokens = "COPILOT_PROVIDER_MAX_OUTPUT_TOKENS";

    /// <summary>
    /// Project the configuration into the ordered list of environment variables the Copilot
    /// CLI expects; the optional token limits are only emitted when set.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> BuildEnvironmentVariables(CodeLocalConfig config)
    {
        List<KeyValuePair<string, string>> variables = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(EnvBaseUrl, config.BaseUrl),
            new KeyValuePair<string, string>(EnvApiKey, config.ApiKey),
            new KeyValuePair<string, string>(EnvModel, config.Model),
            new KeyValuePair<string, string>(EnvWireApi, config.WireApi),
        };

        if (config.MaxPromptTokens is int promptTokens)
        {
            variables.Add(new KeyValuePair<string, string>(
                EnvMaxPromptTokens, promptTokens.ToString(CultureInfo.InvariantCulture)));
        }

        if (config.MaxOutputTokens is int outputTokens)
        {
            variables.Add(new KeyValuePair<string, string>(
                EnvMaxOutputTokens, outputTokens.ToString(CultureInfo.InvariantCulture)));
        }

        return variables;
    }

    /// <summary>
    /// Apply the configuration to the current user's environment so a plain `copilot` picks
    /// it up. On Windows this sets user-level environment variables; on Unix it writes an
    /// env file and sources it from the shell profiles.
    /// </summary>
    public static string Apply(CodeLocalConfig config)
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (KeyValuePair<string, string> pair in BuildEnvironmentVariables(config))
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value); // current process too
            }

            return "Open a NEW terminal (or sign out/in) for the variables to take effect.";
        }

        return ApplyUnix(config);
    }

    /// <summary>
    /// Apply configuration on Unix-like systems (macOS, Linux).
    /// </summary>
    private static string ApplyUnix(CodeLocalConfig config)
    {
        string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string configDirectory = Path.Combine(homeDirectory, ".config", "codelocal");
        Directory.CreateDirectory(configDirectory);
        string envFile = Path.Combine(configDirectory, "copilot.env.sh");

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("# Managed by Code.Local. Do not edit by hand.");
        foreach (KeyValuePair<string, string> pair in BuildEnvironmentVariables(config))
        {
            builder.AppendLine($"export {pair.Key}=\"{pair.Value}\"");
        }
        File.WriteAllText(envFile, builder.ToString());

        string sourceLine = $"[ -f \"{envFile}\" ] && . \"{envFile}\"  # Code.Local";
        List<string> updatedProfiles = new List<string>();

        foreach (string profileName in new[] { ".zshrc", ".bashrc", ".profile" })
        {
            string profilePath = Path.Combine(homeDirectory, profileName);

            if (!File.Exists(profilePath))
            {
                continue;
            }

            if (!File.ReadAllText(profilePath).Contains("# Code.Local"))
            {
                File.AppendAllText(profilePath, Environment.NewLine + sourceLine + Environment.NewLine);
                updatedProfiles.Add(profileName);
            }
        }

        foreach (KeyValuePair<string, string> pair in BuildEnvironmentVariables(config))
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        return updatedProfiles.Count > 0
            ? $"Added a source line to: {string.Join(", ", updatedProfiles)}. Open a new terminal to activate."
            : $"Wrote {envFile}. Add `. {envFile}` to your shell profile to activate.";
    }

    /// <summary>
    /// Read the current configuration from the Copilot environment variables, or null when
    /// the required variables aren't set.
    /// </summary>
    public static CodeLocalConfig? ReadFromEnvironmentOrNull()
    {
        static string? Get(string name)
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(name);
        }

        static int? GetInt(string name)
        {
            string? rawValue = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(name);
            return int.TryParse(rawValue, out int parsedValue) && parsedValue > 0 ? parsedValue : null;
        }

        string? baseUrl = Get(EnvBaseUrl);
        string? model = Get(EnvModel);

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        return new CodeLocalConfig
        {
            BaseUrl = baseUrl,
            Model = model,
            ApiKey = Get(EnvApiKey) ?? "ollama",
            WireApi = Get(EnvWireApi) ?? "completions",
            // The environment doesn't record a runtime; an env-sourced config is read for
            // display only, so treat it as a remote (unmanaged) endpoint.
            RuntimeKey = RemoteRuntime.RuntimeKey,
            MaxPromptTokens = GetInt(EnvMaxPromptTokens),
            MaxOutputTokens = GetInt(EnvMaxOutputTokens),
        };
    }
}
