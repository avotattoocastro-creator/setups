using System;

namespace AvoPerformanceSetupAI.Telemetry;

// ─── Domain types ─────────────────────────────────────────────────────────────

/// <summary>The driving phase detected from the most recent telemetry sample.</summary>
public enum CornerPhase
{
    Straight,
    BrakingZone,
    Cornering,
    Acceleration,
}

/// <summary>
/// Computed telemetry features derived from a window of <see cref="TelemetrySample"/> entries.
/// </summary>
public readonly record struct TelemetryFeatures
{
    /// <summary>
    /// Average front – rear tyre slip-angle delta (rad).
    /// Positive = front slips more than rear → understeer tendency.
    /// </summary>
    public double UndersteerId { get; init; }

    /// <summary>
    /// Average rear – front tyre slip-angle delta (rad), clamped to 0 when positive is front.
    /// Positive = rear slips more than front → oversteer tendency.
    /// </summary>
    public double OversteerId { get; init; }

    /// <summary>
    /// Ratio of rear-to-front average wheel-slip magnitude.
    /// Values significantly above 1.0 indicate rear wheelspin.
    /// </summary>
    public double WheelspinRatio { get; init; }

    /// <summary>
    /// Coefficient of variation of the four brake pressures during braking samples.
    /// 0 = perfectly balanced; higher values indicate uneven brake distribution.
    /// </summary>
    public double BrakeInstability { get; init; }

    /// <summary>
    /// Left – right tyre-temperature difference (°C).
    /// Positive = left side warmer; negative = right side warmer.
    /// </summary>
    public double TyreBalanceLateral { get; init; }

    /// <summary>
    /// Front – rear tyre-temperature difference (°C).
    /// Positive = front warmer; negative = rear warmer.
    /// </summary>
    public double TyreBalanceLongitudinal { get; init; }

    /// <summary>Driving phase inferred from the most recent sample in the window.</summary>
    public CornerPhase Phase { get; init; }
}

// ─── Extractor ────────────────────────────────────────────────────────────────

/// <summary>
/// Stateless calculator that derives <see cref="TelemetryFeatures"/> from a
/// window of <see cref="TelemetrySample"/> entries.
/// </summary>
public static class FeatureExtractor
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// Computes features from the <paramref name="count"/> samples stored at the
    /// start of <paramref name="samples"/>.
    /// Returns a zeroed <see cref="TelemetryFeatures"/> when <paramref name="count"/>
    /// is zero.
    /// </summary>
    public static TelemetryFeatures Extract(TelemetrySample[] samples, int count)
    {
        if (samples is null) throw new ArgumentNullException(nameof(samples));
        if (count <= 0) return new TelemetryFeatures();

        count = Math.Min(count, samples.Length);

        // ── Accumulators ──────────────────────────────────────────────────────

        double sumFrontSlip = 0, sumRearSlip  = 0;
        double sumFrontWheelSlip = 0, sumRearWheelSlip = 0;

        double sumTyreFL = 0, sumTyreFR = 0, sumTyreRL = 0, sumTyreRR = 0;

        double sumBpFL = 0, sumBpFR = 0, sumBpRL = 0, sumBpRR = 0;
        int    brakingSamples = 0;

        for (int i = 0; i < count; i++)
        {
            var s = samples[i];

            // Slip angles — use absolute value; sign depends on corner direction
            sumFrontSlip += (Math.Abs(s.SlipAngleFL) + Math.Abs(s.SlipAngleFR)) * 0.5;
            sumRearSlip  += (Math.Abs(s.SlipAngleRL) + Math.Abs(s.SlipAngleRR)) * 0.5;

    // ── Wheel-slip magnitudes for wheelspin ratio (absolute values to handle deceleration)
            sumFrontWheelSlip += (Math.Abs(s.WheelSlipFL) + Math.Abs(s.WheelSlipFR)) * 0.5;
            sumRearWheelSlip  += (Math.Abs(s.WheelSlipRL) + Math.Abs(s.WheelSlipRR)) * 0.5;

            // Tyre temperatures
            sumTyreFL += s.TyreTempFL;
            sumTyreFR += s.TyreTempFR;
            sumTyreRL += s.TyreTempRL;
            sumTyreRR += s.TyreTempRR;

            // Brake pressure — collect only during actual braking
            if (s.Brake > 0.1f)
            {
                sumBpFL += s.BrakePressureFL;
                sumBpFR += s.BrakePressureFR;
                sumBpRL += s.BrakePressureRL;
                sumBpRR += s.BrakePressureRR;
                brakingSamples++;
            }
        }

        // ── Average ───────────────────────────────────────────────────────────

        var n = (double)count;

        var avgFrontSlip = sumFrontSlip / n;
        var avgRearSlip  = sumRearSlip  / n;

        var avgFrontWheelSlip = sumFrontWheelSlip / n;
        var avgRearWheelSlip  = sumRearWheelSlip  / n;

        var avgTyreFL = sumTyreFL / n;
        var avgTyreFR = sumTyreFR / n;
        var avgTyreRL = sumTyreRL / n;
        var avgTyreRR = sumTyreRR / n;

        // ── Understeer / oversteer ────────────────────────────────────────────

        // Positive delta (front > rear) = understeer; negative = oversteer
        var slipDelta = avgFrontSlip - avgRearSlip;

        var understeerId = Math.Max(0.0, slipDelta);
        var oversteerId  = Math.Max(0.0, -slipDelta);

        // ── Wheelspin ratio ───────────────────────────────────────────────────

        var wheelspinRatio = avgFrontWheelSlip > Epsilon
            ? avgRearWheelSlip / avgFrontWheelSlip
            : 1.0;

        // ── Brake instability ─────────────────────────────────────────────────

        double brakeInstability = 0.0;
        if (brakingSamples > 0)
        {
            var nb   = (double)brakingSamples;
            var bpFL = sumBpFL / nb;
            var bpFR = sumBpFR / nb;
            var bpRL = sumBpRL / nb;
            var bpRR = sumBpRR / nb;
            var mean = (bpFL + bpFR + bpRL + bpRR) * 0.25;

            if (mean > Epsilon)
            {
                var variance = ((bpFL - mean) * (bpFL - mean) +
                                (bpFR - mean) * (bpFR - mean) +
                                (bpRL - mean) * (bpRL - mean) +
                                (bpRR - mean) * (bpRR - mean)) * 0.25;

                brakeInstability = Math.Min(Math.Sqrt(variance) / mean, 1.0);
            }
        }

        // ── Tyre balance ──────────────────────────────────────────────────────

        // Left  = FL + RL ;  Right = FR + RR ;  positive → left warmer
        var tyreBalanceLateral = (avgTyreFL + avgTyreRL) - (avgTyreFR + avgTyreRR);
        // Front = FL + FR ;  Rear  = RL + RR ;  positive → front warmer
        var tyreBalanceLong    = (avgTyreFL + avgTyreFR) - (avgTyreRL + avgTyreRR);

        // ── Corner phase ──────────────────────────────────────────────────────

        var phase = DetectPhase(samples[count - 1]);

        return new TelemetryFeatures
        {
            UndersteerId          = Math.Round(understeerId,      4),
            OversteerId           = Math.Round(oversteerId,       4),
            WheelspinRatio        = Math.Round(wheelspinRatio,    3),
            BrakeInstability      = Math.Round(brakeInstability,  4),
            TyreBalanceLateral    = Math.Round(tyreBalanceLateral, 2),
            TyreBalanceLongitudinal = Math.Round(tyreBalanceLong,  2),
            Phase                 = phase,
        };
    }

    // ── Corner-phase detection ────────────────────────────────────────────────

    private static CornerPhase DetectPhase(TelemetrySample s)
    {
        // Priority: braking → cornering → acceleration → straight
        if (s.Brake > 0.1f)
            return CornerPhase.BrakingZone;

        var absLateral = Math.Abs(s.AccGLateral);

        if (absLateral > 0.3f)
            return s.Throttle > 0.3f ? CornerPhase.Acceleration : CornerPhase.Cornering;

        return CornerPhase.Straight;
    }

    // ── Human-readable log lines ──────────────────────────────────────────────

    /// <summary>
    /// Returns a short list of (tag, message) pairs suitable for appending to an
    /// analysis terminal, derived from the supplied <see cref="TelemetryFeatures"/>.
    /// </summary>
    public static (string Tag, string Msg)[] FormatLog(in TelemetryFeatures f)
    {
        var phaseLabel = f.Phase switch
        {
            CornerPhase.BrakingZone  => "FRENADA",
            CornerPhase.Cornering    => "CURVA",
            CornerPhase.Acceleration => "ACELERACIÓN",
            _                        => "RECTA",
        };

        // Understeer / oversteer — report whichever is dominant
        string behaviorTag, behaviorMsg;
        if (f.UndersteerId > 0.005)
        {
            behaviorTag = "SUBVIRAJE";
            behaviorMsg = $"Subviraje detectado — delta ángulo deslizamiento: +{f.UndersteerId:F4} rad  [{phaseLabel}]";
        }
        else if (f.OversteerId > 0.005)
        {
            behaviorTag = "SOBREVIRAJE";
            behaviorMsg = $"Sobreviraje detectado — delta ángulo deslizamiento: +{f.OversteerId:F4} rad  [{phaseLabel}]";
        }
        else
        {
            behaviorTag = "BALANCE";
            behaviorMsg = $"Balance neutro — deslizamiento frontal/trasero en límites  [{phaseLabel}]";
        }

        // Wheelspin
        var spinTag = f.WheelspinRatio > 1.15
            ? "WHEELSPIN"
            : "TRACCIÓN";
        var spinMsg = f.WheelspinRatio > 1.15
            ? $"Patinamiento trasero — ratio rueda trasera/delantera: {f.WheelspinRatio:F2}"
            : $"Tracción OK — ratio rueda trasera/delantera: {f.WheelspinRatio:F2}";

        // Brake instability
        var brakeTag = f.BrakeInstability > 0.10
            ? "FRENOS!"
            : "FRENOS";
        var brakeMsg = f.BrakeInstability > 0.10
            ? $"Inestabilidad de frenada — coef. variación: {f.BrakeInstability:P0} — revisar reparto"
            : $"Frenada equilibrada — coef. variación: {f.BrakeInstability:P0}";

        // Tyre balance
        var lat  = f.TyreBalanceLateral       >= 0 ? $"izq. +{f.TyreBalanceLateral:F1}°C"  : $"der. +{-f.TyreBalanceLateral:F1}°C";
        var lon  = f.TyreBalanceLongitudinal >= 0 ? $"del. +{f.TyreBalanceLongitudinal:F1}°C" : $"tras. +{-f.TyreBalanceLongitudinal:F1}°C";
        var tyreTag = "NEUMÁTICOS";
        var tyreMsg = $"Balance temp: {lat} / {lon}";

        return
        [
            (behaviorTag, behaviorMsg),
            (spinTag,     spinMsg),
            (brakeTag,    brakeMsg),
            (tyreTag,     tyreMsg),
        ];
    }
}
