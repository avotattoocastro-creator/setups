using System.Collections.Generic;
using AvoPerformanceSetupAI.Models;

namespace AvoPerformanceSetupAI.Telemetry;

/// <summary>
/// Maps a normalized <see cref="FeatureFrame"/> to a prioritized list of
/// <see cref="Proposal"/> objects, each describing one setup adjustment.
/// </summary>
/// <remarks>
/// Rules are evaluated independently; any rule whose feature index exceeds
/// <see cref="Threshold"/> generates a proposal. Results are sorted by
/// <see cref="Proposal.Confidence"/> descending and capped at
/// <see cref="MaxProposals"/> entries, so only the highest-priority changes
/// are surfaced to the driver.
/// </remarks>
public static class RuleEngine
{
    /// <summary>Minimum normalized feature index (0..1) required to trigger a rule.</summary>
    public const float Threshold = 0.15f;

    /// <summary>Maximum number of proposals returned by <see cref="Evaluate"/>.</summary>
    public const int MaxProposals = 6;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates all rules against <paramref name="frame"/> and returns up to
    /// <see cref="MaxProposals"/> proposals sorted by <see cref="Proposal.Confidence"/>
    /// descending. Returns an empty array when no rules are triggered.
    /// </summary>
    public static Proposal[] Evaluate(in FeatureFrame frame)
    {
        var results = new List<Proposal>(11);

        // ── Understeer — entry (braking zone) ────────────────────────────────
        if (frame.UndersteerEntry > Threshold)
            results.Add(Make(
                section:    "ARB",
                parameter:  "FRONT",
                delta:      "+1",
                reason:     "Subviraje en frenada — aumentar ARB delantero 1 click para reducir rolido frontal",
                confidence: frame.UndersteerEntry));

        // ── Understeer — mid-corner (neutral throttle) ────────────────────────
        if (frame.UndersteerMid > Threshold)
            results.Add(Make(
                section:    "SPRINGS",
                parameter:  "FRONT_SPRING",
                delta:      "-1",
                reason:     "Subviraje en curva media — suavizar muelle delantero 1 click para mejorar carga aerodinámica delantera",
                confidence: frame.UndersteerMid));

        // ── Understeer — exit (acceleration phase) ────────────────────────────
        if (frame.UndersteerExit > Threshold)
            results.Add(Make(
                section:    "AERO",
                parameter:  "FRONT_WING",
                delta:      "-1",
                reason:     "Subviraje en aceleración — reducir spoiler delantero 1 click para aumentar velocidad y equilibrio",
                confidence: frame.UndersteerExit));

        // ── Oversteer — entry (braking zone) ─────────────────────────────────
        if (frame.OversteerEntry > Threshold)
            results.Add(Make(
                section:    "ARB",
                parameter:  "REAR",
                delta:      "-1",
                reason:     "Sobreviraje en entrada — reducir ARB trasero 1 click para suavizar rotación trasera",
                confidence: frame.OversteerEntry));

        // ── Oversteer — exit (acceleration phase) ────────────────────────────
        if (frame.OversteerExit > Threshold)
            results.Add(Make(
                section:    "ELECTRONICS",
                parameter:  "DIFF_ACC",
                delta:      "+2",
                reason:     "Sobreviraje en salida — aumentar diferencial de aceleración para controlar apertura trasera",
                confidence: frame.OversteerExit));

        // ── Rear wheelspin ────────────────────────────────────────────────────
        if (frame.WheelspinRatioRear > Threshold)
            results.Add(Make(
                section:    "ELECTRONICS",
                parameter:  "TRACTION_CONTROL",
                delta:      "+1",
                reason:     "Patinamiento trasero — aumentar control de tracción 1 nivel para reducir pérdida de potencia",
                confidence: frame.WheelspinRatioRear));

        // ── Front wheel lockup (braking) ──────────────────────────────────────
        if (frame.LockupRatioFront > Threshold)
            results.Add(Make(
                section:    "BRAKES",
                parameter:  "BRAKE_BIAS",
                delta:      "-1",
                reason:     "Bloqueo ruedas delanteras — reducir reparto de frenos delante 1 % para equilibrar la frenada",
                confidence: frame.LockupRatioFront));

        // ── Front/rear tyre temperature imbalance (overheating front) ─────────
        if (frame.TyreTempDeltaFR > Threshold)
            results.Add(Make(
                section:    "TYRES",
                parameter:  "PRESSURE_LF",
                delta:      "-0.05",
                reason:     "Temperatura neumático delantero alta — reducir presión delantera 0.05 bar para ampliar huella",
                confidence: frame.TyreTempDeltaFR));

        // ── Left/right tyre temperature imbalance ─────────────────────────────
        if (frame.TyreTempDeltaLR > Threshold)
            results.Add(Make(
                section:    "ALIGNMENT",
                parameter:  "CAMBER_LF",
                delta:      "-0.1",
                reason:     "Desequilibrio térmico izquierda-derecha — revisar camber para igualar temperatura lateral",
                confidence: frame.TyreTempDeltaLR));

        // ── Brake pressure instability ────────────────────────────────────────
        if (frame.BrakeStabilityIndex > Threshold)
            results.Add(Make(
                section:    "BRAKES",
                parameter:  "BRAKE_POWER",
                delta:      "-1",
                reason:     "Inestabilidad de frenada — revisar reparto de presión entre ejes (CoV elevado)",
                confidence: frame.BrakeStabilityIndex));

        // ── Suspension oscillation (bump/rebound too stiff or too soft) ────────
        if (frame.SuspensionOscillationIndex > Threshold)
            results.Add(Make(
                section:    "DAMPERS",
                parameter:  "BUMP_REAR",
                delta:      "+1",
                reason:     "Oscilación de suspensión trasera — aumentar amortiguador de compresión trasero 1 click",
                confidence: frame.SuspensionOscillationIndex));

        if (results.Count == 0)
            return [];

        // Sort by confidence descending, cap at MaxProposals
        results.Sort(static (a, b) => b.Confidence.CompareTo(a.Confidence));
        if (results.Count > MaxProposals)
            results.RemoveRange(MaxProposals, results.Count - MaxProposals);

        return [.. results];
    }

    // ── Factory helper ────────────────────────────────────────────────────────

    private static Proposal Make(
        string section,
        string parameter,
        string delta,
        string reason,
        float  confidence)
        => new()
        {
            Section    = section,
            Parameter  = parameter,
            From       = string.Empty, // live values are unknown without a loaded .ini
            To         = string.Empty,
            Delta      = delta,
            Reason     = reason,
            Confidence = confidence,
        };
}
