using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeLocal.Commands;

/// <summary>
/// Wraps the raw parsed command-line dictionary and binds typed option values. Every key a
/// command reads is recorded, so <see cref="EnsureNoUnknownOptions"/> can fail fast on any
/// option the command doesn't recognize (a typo or a removed flag) instead of ignoring it.
/// </summary>
internal sealed class CommandLineOptions
{
    private readonly IReadOnlyDictionary<string, string> _values;
    private readonly HashSet<string> _readKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a wrapper over the raw <c>--key value</c> pairs parsed from the command line.
    /// </summary>
    /// <param name="values">The parsed option dictionary.</param>
    public CommandLineOptions(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
    }

    /// <summary>
    /// Returns true when the given flag is present (e.g. <c>--persist</c>).
    /// </summary>
    public bool HasFlag(string key)
    {
        _readKeys.Add(key);
        return _values.ContainsKey(key);
    }

    /// <summary>
    /// Returns the option's value when it is present and non-empty, or null otherwise.
    /// </summary>
    public string? Text(string key)
    {
        _readKeys.Add(key);

        if (_values.TryGetValue(key, out string? optionValue) && !string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        return null;
    }

    /// <summary>
    /// Parses a strictly positive integer option, or null when it is absent. Throws when the
    /// option is present but not a positive integer, so typos fail fast instead of being ignored.
    /// </summary>
    public int? PositiveInteger(string key)
    {
        _readKeys.Add(key);

        if (!_values.TryGetValue(key, out string? optionValue))
        {
            return null;
        }

        if (int.TryParse(optionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedNumber) && parsedNumber > 0)
        {
            return parsedNumber;
        }

        throw new InvalidOperationException($"--{key} must be a positive integer (got '{optionValue}').");
    }

    /// <summary>
    /// Parses a strictly positive floating-point option, or null when it is absent. Throws when
    /// the option is present but not a positive number, so typos fail fast instead of being ignored.
    /// </summary>
    public double? PositiveDouble(string key)
    {
        _readKeys.Add(key);

        if (!_values.TryGetValue(key, out string? optionValue))
        {
            return null;
        }

        if (double.TryParse(optionValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedNumber) && parsedNumber > 0)
        {
            return parsedNumber;
        }

        throw new InvalidOperationException($"--{key} must be a positive number (got '{optionValue}').");
    }

    /// <summary>
    /// Throws when an option was supplied that the command never read, so an unknown or removed
    /// flag fails fast instead of being silently ignored. Call after binding all known options.
    /// </summary>
    public void EnsureNoUnknownOptions()
    {
        foreach (string key in _values.Keys)
        {
            if (!_readKeys.Contains(key))
            {
                throw new InvalidOperationException(
                    $"unknown option '--{key}'. Run `codelocal --help` to see available options.");
            }
        }
    }
}
