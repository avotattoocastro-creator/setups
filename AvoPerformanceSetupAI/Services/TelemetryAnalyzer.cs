using System;
using System.Collections.Generic;
using System.Linq;
using AvoPerformanceSetupAI.Models;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// Analyses a rolling window of Assetto Corsa telemetry samples and generates
/// setup proposals whenever a recurring handling problem is detected.
/// <para>
/// Issues detected: oversteer, understeer, tyre temperature out of range,
/// reduced track grip (rain/dirt), and session-specific optimisation
/// (qualifying lap time vs race tyre degradation).
/// </para>
/// <para>
/// Proposals are produced by the existing <see cref="NlpService"/> rule engine
/// and scored by the shared <see cref="MlSetupOptimizer"/> neural network.
/// </para>
/// </summary>
public sealed class TelemetryAnalyzer
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Number of samples kept in the rolling window (60 @ 10 Hz = 6 seconds).</summary>
    private const int WindowSize = 60;

    /// <summary>Minimum valid (on-track, high-speed) samples before analysis runs.</summary>
    private const int MinSamples = 20;

    /// <summary>Minimum speed (km/h) for a sample to count as "on track".</summary>
    private const float MinSpeedKmh = 40f;

    // Issue score thresholds calibrated against typical AC telemetry values
    private const float OversteerThreshold  = 1.5f;
    private const float UndersteerThreshold = 1.5f;
    private const float TyreHeatThreshold   = 0.15f;
    private const float WetGripThreshold    = 0.25f;   // surfaceGrip deviation from 1.0

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly Queue<AcTelemetrySnapshot> _window = new();
    private readonly MlSetupOptimizer           _optimizer;

    public TelemetryAnalyzer(MlSetupOptimizer optimizer)
    {
        _optimizer = optimizer;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Adds a live sample to the rolling window. Ignores non-live samples.</summary>
    public void AddSample(AcTelemetrySnapshot snapshot)
    {
        if (!snapshot.IsLive) return;
        _window.Enqueue(snapshot);
        while (_window.Count > WindowSize)
            _window.Dequeue();
    }

    /// <summary>
    /// Analyses the current window and returns a list of setup proposals.
    /// Returns an empty list when not enough valid (on-track) samples are available,
    /// or when no clear issue can be identified.
    /// </summary>
    /// <param name="iniEntries">
    /// INI entries from the currently loaded setup file (may be empty; in that case
    /// generic textual proposals are returned by the NLP service).
    /// </param>
    public List<Proposal> Analyse(IReadOnlyList<IniEntry> iniEntries)
    {
        var valid = _window
            .Where(s => s.SpeedKmh >= MinSpeedKmh && !s.IsInPit)
            .ToList();

        if (valid.Count < MinSamples)
            return new List<Proposal>();

        // ── Compute issue scores ──────────────────────────────────────────────

        float oversteerScore  = ComputeOversteerScore(valid);
        float understeerScore = ComputeUndersteerScore(valid);
        float tyreHeatScore   = ComputeTyreHeatScore(valid);
        float wetScore        = 1.0f - valid.Average(s => s.SurfaceGrip);

        // ── Select dominant intent ────────────────────────────────────────────

        string intent = DetermineIntent(oversteerScore, understeerScore,
                                        tyreHeatScore, wetScore, valid);
        if (intent.Length == 0)
            return new List<Proposal>();

        // ── Generate proposals via the NLP rule engine + neural network ───────

        var proposals = NlpService.GetProposals(intent, iniEntries, _optimizer);

        // Annotate each proposal with the telemetry context that triggered it
        string prefix = BuildPrefix(intent, oversteerScore, understeerScore,
                                    tyreHeatScore, wetScore, valid);

        foreach (var p in proposals)
        {
            if (!p.Reason.StartsWith("📡"))
                p.Reason = $"📡 {prefix} — {p.Reason}";
        }

        return proposals;
    }

    // ── Score helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Oversteer score: mean positive difference between rear and front wheel slip
    /// across high-speed samples.  Higher = more rear slip = more oversteer.
    /// </summary>
    private static float ComputeOversteerScore(List<AcTelemetrySnapshot> samples)
    {
        float sum = 0;
        int   n   = 0;
        foreach (var s in samples)
        {
            float rear  = (s.WheelSlip[2] + s.WheelSlip[3]) * 0.5f;
            float front = (s.WheelSlip[0] + s.WheelSlip[1]) * 0.5f;
            float diff  = rear - front;
            if (diff > 0) { sum += diff; n++; }
        }
        return n == 0 ? 0f : sum / n;
    }

    /// <summary>
    /// Understeer score: mean positive difference between front and rear wheel slip
    /// on samples where the driver is steering.  Higher = more push / understeer.
    /// </summary>
    private static float ComputeUndersteerScore(List<AcTelemetrySnapshot> samples)
    {
        float sum = 0;
        int   n   = 0;
        foreach (var s in samples)
        {
            // Only consider samples with meaningful steering input
            if (MathF.Abs(s.SteerAngle) < 0.15f) continue;

            float front = (s.WheelSlip[0] + s.WheelSlip[1]) * 0.5f;
            float rear  = (s.WheelSlip[2] + s.WheelSlip[3]) * 0.5f;
            float diff  = front - rear;
            if (diff > 0) { sum += diff; n++; }
        }
        return n == 0 ? 0f : sum / n;
    }

    /// <summary>
    /// Tyre heat score: average normalised deviation of all four tyre core temperatures
    /// from the optimal working range (80–105 °C).
    /// </summary>
    private static float ComputeTyreHeatScore(List<AcTelemetrySnapshot> samples)
    {
        const float optMin = 80f;
        const float optMax = 105f;

        float totalDev = 0;
        int   count    = 0;
        foreach (var s in samples)
        {
            for (int i = 0; i < 4; i++)
            {
                float t = s.TyreCoreTemp[i];
                if (t <= 0) continue;  // sensor not present / car stationary
                if      (t < optMin) totalDev += (optMin - t) / optMin;
                else if (t > optMax) totalDev += (t - optMax) / optMax;
                count++;
            }
        }
        return count == 0 ? 0f : totalDev / count;
    }

    // ── Intent selection ──────────────────────────────────────────────────────

    private static string DetermineIntent(
        float overS, float underS, float tyreH, float wetS,
        List<AcTelemetrySnapshot> samples)
    {
        // Wet/low-grip conditions take priority over handling balance issues
        if (wetS > WetGripThreshold)
            return "wet_setup";

        if (overS  > OversteerThreshold  && overS  > underS) return "oversteer_fix";
        if (underS > UndersteerThreshold && underS > overS)  return "understeer_fix";
        if (tyreH  > TyreHeatThreshold)                      return "mechanical_grip";

        // Fall back to session-type optimisation when no handling issue is dominant
        var latest = samples.LastOrDefault();
        return latest?.SessionType switch
        {
            1 => "qualify",
            2 => "race",
            _ => string.Empty,  // practice / no session — nothing to report
        };
    }

    // ── Proposal annotation ───────────────────────────────────────────────────

    private static string BuildPrefix(
        string intent, float overS, float underS, float tyreH, float wetS,
        List<AcTelemetrySnapshot> samples)
    {
        var latest = samples.LastOrDefault();
        string session = latest?.SessionType switch
        {
            0 => "práctica",
            1 => "clasificación",
            2 => "carrera",
            3 => "vuelta rápida",
            _ => "sesión",
        };

        return intent switch
        {
            "oversteer_fix"   => $"Sobreviraje detectado (índice {overS:F1}) en {session}",
            "understeer_fix"  => $"Subviraje detectado (índice {underS:F1}) en {session}",
            "wet_setup"       => $"Grip reducido: pista al {(1f - wetS):P0} en {session}",
            "mechanical_grip" => $"Temperatura de neumáticos fuera de rango en {session}",
            "qualify"         => $"Sesión de clasificación — optimizando vuelta rápida",
            "race"            => $"Carrera — optimizando degradación y ritmo",
            _                 => $"Análisis de {session}",
        };
    }
}
