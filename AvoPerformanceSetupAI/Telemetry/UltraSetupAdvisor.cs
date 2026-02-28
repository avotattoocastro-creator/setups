using System;
using System.Collections.Generic;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.Profiles;
using AvoPerformanceSetupAI.Reference;

namespace AvoPerformanceSetupAI.Telemetry;

// ── Output types ──────────────────────────────────────────────────────────────

/// <summary>Risk classification for a proposed setup change.</summary>
public enum RiskLevel
{
    /// <summary>Change is conservative; unlikely to destabilize the car.</summary>
    Low,

    /// <summary>Change has a moderate effect; should be validated on-track.</summary>
    Medium,

    /// <summary>Change is aggressive; high potential for unexpected handling shift.</summary>
    High,
}

/// <summary>
/// An enriched setup-change proposal produced by <see cref="UltraSetupAdvisor"/>.
/// Extends the baseline <see cref="Proposal"/> with estimated lap-time and
/// score impact.
/// </summary>
public sealed class AdvisedProposal
{
    // ── Baseline proposal fields (mirrors Proposal) ───────────────────────────

    /// <summary>Setup section, e.g. "ARB", "AERO", "ELECTRONICS".</summary>
    public string Section    { get; init; } = string.Empty;

    /// <summary>Parameter within the section, e.g. "FRONT", "DIFF_ACC".</summary>
    public string Parameter  { get; init; } = string.Empty;

    /// <summary>Signed adjustment step, e.g. "+1", "-0.05".</summary>
    public string Delta      { get; init; } = string.Empty;

    /// <summary>Human-readable explanation.</summary>
    public string Reason     { get; init; } = string.Empty;

    /// <summary>Rule-engine confidence (0..1), possibly reduced by discriminator gating.</summary>
    public float  Confidence { get; init; }

    // ── Impact estimates ──────────────────────────────────────────────────────

    /// <summary>
    /// Rough estimated lap-time delta in seconds (positive = faster).
    /// Derived from a simple heuristic table; not a simulation result.
    /// </summary>
    public float EstimatedLapDeltaSec { get; init; }

    /// <summary>
    /// Estimated overall <see cref="DrivingScores.OverallScore"/> delta
    /// (positive = score improvement).
    /// </summary>
    public float EstimatedScoreDelta  { get; init; }

    /// <summary>Risk level of this change.</summary>
    public RiskLevel RiskLevel { get; init; }
}

// ── SimulationImpactEstimator (private heuristic) ────────────────────────────

/// <summary>
/// Lightweight heuristic estimator that converts a normalized <see cref="FeatureFrame"/>
/// index and a section/parameter change into expected sub-score deltas.
/// All values are rough approximations; the goal is relative ranking, not accuracy.
/// </summary>
file static class SimulationImpactEstimator
{
    // Impact table: (Section:Parameter) -> (balanceGain, tractionGain, brakeGain, lapDeltaSec, risk)
    private static readonly Dictionary<string, (float balance, float traction, float brake, float lapDelta, RiskLevel risk)>
        _table = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ARB:FRONT"]              = (12f,  0f,  2f, 0.08f, RiskLevel.Low),
            ["ARB:REAR"]               = (10f,  2f,  0f, 0.07f, RiskLevel.Low),
            ["SPRINGS:FRONT_SPRING"]   = (8f,   0f,  3f, 0.06f, RiskLevel.Medium),
            ["AERO:FRONT_WING"]        = (10f,  0f,  0f, 0.12f, RiskLevel.Medium),
            ["ELECTRONICS:DIFF_ACC"]   = (5f,  12f,  0f, 0.10f, RiskLevel.Medium),
            ["ELECTRONICS:TRACTION_CONTROL"] = (0f, 8f, 0f, 0.05f, RiskLevel.Low),
            ["BRAKES:BRAKE_BIAS"]      = (0f,   0f, 14f, 0.09f, RiskLevel.Medium),
            ["BRAKES:BRAKE_POWER"]     = (0f,   0f, 10f, 0.07f, RiskLevel.Medium),
            ["TYRES:PRESSURE_LF"]      = (3f,   4f,  3f, 0.05f, RiskLevel.Low),
            ["ALIGNMENT:CAMBER_LF"]    = (4f,   3f,  2f, 0.06f, RiskLevel.High),
            ["DAMPERS:BUMP_REAR"]      = (3f,   2f,  2f, 0.04f, RiskLevel.Low),
        };

    public static (float balanceDelta, float tractionDelta, float brakeDelta,
                   float lapDelta, RiskLevel risk)
        Estimate(string section, string parameter, float featureIndex)
    {
        var key = $"{section}:{parameter}";
        if (!_table.TryGetValue(key, out var row))
            return (0f, 0f, 0f, 0f, RiskLevel.Low);

        // Scale impact linearly with feature severity; cap at 1 to avoid inflating minor issues
        float scale = Math.Clamp(featureIndex * 1.5f, 0.2f, 1.0f);
        return (row.balance * scale,
                row.traction * scale,
                row.brake    * scale,
                row.lapDelta * scale,
                row.risk);
    }
}

// ── UltraSetupAdvisor ─────────────────────────────────────────────────────────

/// <summary>
/// High-level setup advisor that:
/// <list type="number">
///   <item>Generates up to 10–15 candidate <see cref="Proposal"/> objects using
///     the <see cref="RuleEngine"/> as a baseline.</item>
///   <item>Scores each candidate with <c>SimulationImpactEstimator</c>.</item>
///   <item>Applies discriminator gating via <see cref="DriverVsSetupDiscriminator.ApplyGate"/>.</item>
///   <item>Returns the top <see cref="MaxTopProposals"/> <see cref="AdvisedProposal"/>
///     objects sorted by <see cref="AdvisedProposal.EstimatedLapDeltaSec"/> descending.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The estimated lap delta and score delta are rough heuristics, not simulation
/// results. They are useful for relative ranking only. More accurate estimates
/// require a vehicle-dynamics model beyond the scope of this module.
/// </para>
/// <para>
/// Thread-safety: this class is stateless. All methods are safe to call from
/// any thread simultaneously.
/// </para>
/// </remarks>
public static class UltraSetupAdvisor
{
    /// <summary>Maximum number of top proposals returned by <see cref="Advise"/>.</summary>
    public const int MaxTopProposals = 3;

    /// <summary>Maximum number of candidates generated before scoring.</summary>
    public const int MaxCandidates = 15;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates scored setup proposals from the supplied context.
    /// Returns an empty array when no rules are triggered.
    /// </summary>
    /// <param name="frame">Aggregate <see cref="FeatureFrame"/> from the current session.</param>
    /// <param name="corners">Last completed corners (used for cross-validation).</param>
    /// <param name="scores">
    /// Current <see cref="DrivingScores"/> (used for relative scoring of improvements).
    /// When <see langword="null"/> the estimator still works but score deltas are scaled down.
    /// </param>
    /// <param name="rootCause">Discriminator result; gates proposals when DriverLikely.</param>
    /// <param name="profile">Optional car/track profile passed to <see cref="RuleEngine"/>.</param>
    public static AdvisedProposal[] Advise(
        in FeatureFrame             frame,
        ReadOnlySpan<CornerSummary> corners,
        DrivingScores?              scores,
        in RootCauseResult          rootCause,
        CarTrackProfile?            profile = null)
    {
        // ── Step 1: generate candidates from RuleEngine ───────────────────────
        var rawProposals = profile != null
            ? RuleEngine.Evaluate(in frame, profile)
            : RuleEngine.Evaluate(in frame);

        // Expand with corner-phase rules to reach MaxCandidates if possible
        var candidates = new List<Proposal>(rawProposals);
        EnrichFromCorners(candidates, corners, frame);

        // Cap at MaxCandidates
        if (candidates.Count > MaxCandidates)
            candidates.RemoveRange(MaxCandidates, candidates.Count - MaxCandidates);

        // ── Step 2: apply discriminator gating ────────────────────────────────
        var gated = DriverVsSetupDiscriminator.ApplyGate([.. candidates], in rootCause);

        // ── Step 3: score each candidate ──────────────────────────────────────
        var advised = new List<AdvisedProposal>(gated.Length);
        float baseOverall = scores?.OverallScore ?? 50f;

        foreach (var p in gated)
        {
            // Use the proposal confidence as a proxy for the feature index severity
            var (balDelta, tracDelta, brkDelta, lapDelta, risk) =
                SimulationImpactEstimator.Estimate(p.Section, p.Parameter, p.Confidence);

            // Overall score delta: weighted combination of sub-score gains
            float scoreDelta = (balDelta  * 0.35f +
                                tracDelta * 0.25f +
                                brkDelta  * 0.20f);

            // Scale score delta so it's relative to current weakness
            if (scores != null)
            {
                // Amplify proposals that address the most deficient area
                if (balDelta > 0 && scores.BalanceScore < 60f)   scoreDelta *= 1.2f;
                if (tracDelta > 0 && scores.TractionScore < 60f) scoreDelta *= 1.2f;
                if (brkDelta > 0 && scores.BrakeScore < 60f)     scoreDelta *= 1.2f;
            }

            // Penalize high-risk changes
            if (risk == RiskLevel.High)   { lapDelta   *= 0.70f; scoreDelta *= 0.70f; }
            if (risk == RiskLevel.Medium) { lapDelta   *= 0.90f; scoreDelta *= 0.90f; }

            advised.Add(new AdvisedProposal
            {
                Section               = p.Section,
                Parameter             = p.Parameter,
                Delta                 = p.Delta,
                Reason                = p.Reason,
                Confidence            = p.Confidence,
                EstimatedLapDeltaSec  = lapDelta,
                EstimatedScoreDelta   = Math.Clamp(scoreDelta, 0f, 30f),
                RiskLevel             = risk,
            });
        }

        // ── Step 4: sort by estimated lap delta descending, cap at MaxTopProposals ──
        advised.Sort(static (a, b) => b.EstimatedLapDeltaSec.CompareTo(a.EstimatedLapDeltaSec));
        if (advised.Count > MaxTopProposals)
            advised.RemoveRange(MaxTopProposals, advised.Count - MaxTopProposals);

        return [.. advised];
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Adds extra proposals derived from per-phase corner frame data.
    /// These supplement the aggregate-frame rules when corner-phase signals
    /// are more pronounced than the aggregate.
    /// </summary>
    private static void EnrichFromCorners(
        List<Proposal>              candidates,
        ReadOnlySpan<CornerSummary> corners,
        in FeatureFrame             frame)
    {
        if (corners.Length == 0) return;

        // Average phase-specific signals across the last corners
        float avgUsEntry = 0, avgUsMid = 0, avgUsExit = 0;
        float avgOsEntry = 0, avgOsExit = 0;
        float avgWheelspin = 0, avgLockup = 0;

        foreach (var c in corners)
        {
            avgUsEntry   += c.EntryFrame.UndersteerEntry;
            avgUsMid     += c.MidFrame.UndersteerMid;
            avgUsExit    += c.ExitFrame.UndersteerExit;
            avgOsEntry   += c.EntryFrame.OversteerEntry;
            avgOsExit    += c.ExitFrame.OversteerExit;
            avgWheelspin += c.ExitFrame.WheelspinRatioRear;
            avgLockup    += c.EntryFrame.LockupRatioFront;
        }

        float n = corners.Length;
        avgUsEntry   /= n; avgUsMid   /= n; avgUsExit  /= n;
        avgOsEntry   /= n; avgOsExit  /= n;
        avgWheelspin /= n; avgLockup  /= n;

        const float EnrichThreshold = 0.12f;

        // Only add a proposal if it's not already in the candidate list
        if (avgUsMid > EnrichThreshold && !ContainsKey(candidates, "SPRINGS", "FRONT_SPRING"))
            candidates.Add(Make("SPRINGS", "FRONT_SPRING", "-1",
                "Subviraje mid-corner persistente en curva — suavizar muelle delantero", avgUsMid));

        if (avgUsExit > EnrichThreshold && !ContainsKey(candidates, "AERO", "FRONT_WING"))
            candidates.Add(Make("AERO", "FRONT_WING", "-1",
                "Subviraje en salida persistente — reducir ala delantera", avgUsExit));

        if (avgOsExit > EnrichThreshold && !ContainsKey(candidates, "ELECTRONICS", "DIFF_ACC"))
            candidates.Add(Make("ELECTRONICS", "DIFF_ACC", "+2",
                "Sobreviraje en salida persistente — aumentar diferencial", avgOsExit));

        if (avgWheelspin > EnrichThreshold && !ContainsKey(candidates, "ELECTRONICS", "TRACTION_CONTROL"))
            candidates.Add(Make("ELECTRONICS", "TRACTION_CONTROL", "+1",
                "Wheelspin trasero en salida de curva", avgWheelspin));

        if (avgLockup > EnrichThreshold && !ContainsKey(candidates, "BRAKES", "BRAKE_BIAS"))
            candidates.Add(Make("BRAKES", "BRAKE_BIAS", "-1",
                "Bloqueo delantero recurrente — reducir reparto de frenos", avgLockup));
    }

    private static bool ContainsKey(List<Proposal> list, string section, string parameter)
    {
        foreach (var p in list)
            if (string.Equals(p.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Parameter, parameter, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static Proposal Make(string section, string parameter, string delta,
                                  string reason, float confidence)
        => new()
        {
            Section    = section,
            Parameter  = parameter,
            From       = string.Empty,
            To         = string.Empty,
            Delta      = delta,
            Reason     = reason,
            Confidence = confidence,
        };
}
