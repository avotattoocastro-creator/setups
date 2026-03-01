namespace AvoPerformanceSetupAI.Models;

public enum SetupDiffKind { Added, Removed, Changed }

public sealed class SetupDiffEntry
{
    public SetupDiffKind Kind { get; init; }
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Section { get; init; } = "";
    public string? Group { get; init; }
    public double? OldValue { get; init; }
    public double? NewValue { get; init; }
    public double? Delta { get; init; }
    public double? DeltaPercent { get; init; }

    // ── Display helpers for x:Bind ────────────────────────────────────────────

    public string KindDisplay => Kind switch
    {
        SetupDiffKind.Added   => "+ Added",
        SetupDiffKind.Removed => "- Removed",
        _                     => "~ Changed",
    };

    public string OldValueDisplay => OldValue.HasValue
        ? OldValue.Value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;

    public string NewValueDisplay => NewValue.HasValue
        ? NewValue.Value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;

    public string DeltaDisplay => Delta.HasValue
        ? (Delta.Value >= 0 ? "+" : "") +
          Delta.Value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;

    public string DeltaPercentDisplay => DeltaPercent.HasValue
        ? (DeltaPercent.Value >= 0 ? "+" : "") +
          DeltaPercent.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "%"
        : string.Empty;
}
