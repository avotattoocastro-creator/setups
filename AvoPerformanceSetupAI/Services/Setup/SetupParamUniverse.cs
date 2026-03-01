using System.Collections.Generic;
using System.Globalization;
using AvoPerformanceSetupAI.Models;

namespace AvoPerformanceSetupAI.Services.Setup;

/// <summary>Metadata for a single key parsed from a setup INI file.</summary>
public sealed class SetupKeyInfo
{
    public string  Section      { get; init; } = string.Empty;
    public string  Key          { get; init; } = string.Empty;
    public string  RawValue     { get; init; } = string.Empty;
    public double? NumericValue { get; init; }
    public bool    IsNumeric    { get; init; }
}

/// <summary>
/// The complete set of sections and keys that are present in one setup INI file.
/// Used to restrict AI proposal generation to parameters that actually exist,
/// preventing "Parámetro no encontrado" errors when applying proposals.
/// </summary>
public sealed class SetupParamUniverse
{
    /// <summary>Car folder name.</summary>
    public string Car   { get; init; } = string.Empty;

    /// <summary>Track folder name.</summary>
    public string Track { get; init; } = string.Empty;

    /// <summary>INI file name.</summary>
    public string File  { get; init; } = string.Empty;

    /// <summary>Total distinct (section, key) pairs in the file.</summary>
    public int KeyCount     { get; init; }

    /// <summary>Number of (section, key) pairs whose value is numeric.</summary>
    public int NumericCount { get; init; }

    /// <summary>Number of distinct section names.</summary>
    public int SectionCount { get; init; }

    // Normalized upper-case (section, key) pairs for O(1) lookup.
    private readonly HashSet<(string section, string key)> _lookup;

    private SetupParamUniverse(HashSet<(string, string)> lookup, int keyCount, int numericCount, int sectionCount)
    {
        _lookup      = lookup;
        KeyCount     = keyCount;
        NumericCount = numericCount;
        SectionCount = sectionCount;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the (section, key) pair exists in this setup file.
    /// Comparison is case-insensitive.
    /// </summary>
    public bool Contains(string section, string key) =>
        _lookup.Contains((section.ToUpperInvariant(), key.ToUpperInvariant()));

    /// <summary>
    /// Builds a <see cref="SetupParamUniverse"/> from a list of <see cref="IniEntry"/>
    /// objects produced by <c>SetupIniParser</c>.
    /// </summary>
    public static SetupParamUniverse Build(string car, string track, string file, IEnumerable<IniEntry> entries)
    {
        var lookup   = new HashSet<(string, string)>();
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int numeric  = 0;

        foreach (var e in entries)
        {
            var sec = (e.Section ?? string.Empty).ToUpperInvariant();
            var key = (e.Key     ?? string.Empty).ToUpperInvariant();
            lookup.Add((sec, key));
            sections.Add(sec);
            if (double.TryParse(e.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                numeric++;
        }

        return new SetupParamUniverse(lookup, lookup.Count, numeric, sections.Count)
        {
            Car   = car,
            Track = track,
            File  = file,
        };
    }
}
