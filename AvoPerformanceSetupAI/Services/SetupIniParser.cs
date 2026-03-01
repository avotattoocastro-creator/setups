using System.Collections.Generic;
using System.IO;
using AvoPerformanceSetupAI.Models;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// Parses Assetto Corsa / ACC setup <c>.ini</c> files into a flat list of <see cref="IniEntry"/> records,
/// preserving the section context of every key so proposals can be applied back accurately.
/// </summary>
public static class SetupIniParser
{
    /// <summary>
    /// Reads <paramref name="filePath"/> and returns every key=value pair together with its INI section.
    /// Comment lines (starting with <c>;</c> or <c>//</c>) and blank lines are ignored.
    /// </summary>
    public static List<IniEntry> Parse(string filePath)
        => ParseLines(File.ReadLines(filePath));

    /// <summary>
    /// Parses INI content already loaded as a string.
    /// Accepts both <c>\r\n</c> and <c>\n</c> line endings.
    /// </summary>
    public static List<IniEntry> ParseText(string text)
        => ParseLines(text.Split('\n'));

    private static List<IniEntry> ParseLines(IEnumerable<string> lines)
    {
        var entries = new List<IniEntry>();
        var currentSection = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            // Skip blank lines and comments
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//"))
                continue;

            // Section header
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            // Key=value pair
            var eqIdx = line.IndexOf('=');
            if (eqIdx > 0)
            {
                entries.Add(new IniEntry
                {
                    Section = currentSection,
                    Key     = line[..eqIdx].Trim(),
                    Value   = line[(eqIdx + 1)..].Trim()
                });
            }
        }

        return entries;
    }
}
