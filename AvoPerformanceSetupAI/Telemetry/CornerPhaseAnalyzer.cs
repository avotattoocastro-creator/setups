using System;
using System.Collections.Generic;

namespace AvoPerformanceSetupAI.Telemetry;

// ─── CornerSummary ────────────────────────────────────────────────────────────

/// <summary>
/// Per-corner aggregated analysis produced by <see cref="CornerPhaseAnalyzer"/>.
/// All index fields (understeer, oversteer, wheelspin, lockup) are normalized to
/// 0..1 against the same thresholds used by <see cref="FeatureExtractor"/>.
/// </summary>
public readonly record struct CornerSummary
{
    /// <summary>Sequential index of this corner within the analysis window (0 = oldest).</summary>
    public int      CornerIndex     { get; init; }

    /// <summary>
    /// Timestamp of the first sample of this corner, mirroring
    /// <see cref="TelemetrySample.Timestamp"/> (set to <see cref="DateTime.UtcNow"/>
    /// by <see cref="AcTelemetryReader"/>).
    /// </summary>
    public DateTime StartTimestamp  { get; init; }

    /// <summary>Normalized track position (0..1) at the corner apex (sample with peak lateral G).</summary>
    public float    LapPos          { get; init; }

    /// <summary>Total corner duration in milliseconds.</summary>
    public int      DurationMs      { get; init; }

    /// <summary>Peak absolute lateral G recorded in this corner.</summary>
    public float    PeakLateralG    { get; init; }

    // ── Phase-specific understeer / oversteer (0..1) ──────────────────────────

    /// <summary>Understeer index during the corner entry (braking) phase (0..1).</summary>
    public float    UndersteerEntry { get; init; }

    /// <summary>Understeer index during the mid-corner (neutral throttle) phase (0..1).</summary>
    public float    UndersteerMid   { get; init; }

    /// <summary>Understeer index during the corner exit (acceleration) phase (0..1).</summary>
    public float    UndersteerExit  { get; init; }

    /// <summary>Oversteer index during the corner entry phase (0..1).</summary>
    public float    OversteerEntry  { get; init; }

    /// <summary>Oversteer index during the corner exit phase (0..1).</summary>
    public float    OversteerExit   { get; init; }

    // ── Wheelspin / lockup (0..1) ─────────────────────────────────────────────

    /// <summary>Rear wheelspin index for this corner (0..1).</summary>
    public float    WheelspinRear   { get; init; }

    /// <summary>Front wheel-lockup index during the braking phase (0..1).</summary>
    public float    LockupFront     { get; init; }

    /// <summary>Primary issue tag: "US" (understeer), "OS" (oversteer), "SPIN", "LOCK", or "OK".</summary>
    public string   Dominant        { get; init; }
}

// ─── CornerPhaseAnalyzer ──────────────────────────────────────────────────────

/// <summary>
/// Segments a stream of <see cref="TelemetrySample"/> entries into discrete corner
/// events using lateral G, steering angle rate-of-change, and brake/throttle
/// thresholds. Each identified corner is characterized by a phase-aware
/// <see cref="CornerSummary"/> with aggregated feature indices.
/// </summary>
/// <remarks>
/// Corner detection uses a two-stage hysteresis state machine driven by
/// <see cref="LateralGThreshold"/>: a corner starts after <see cref="HysteresisSamples"/>
/// consecutive samples above the threshold, and ends after the same number of
/// consecutive samples below it. Within each corner the three sub-phases are
/// classified sample-by-sample using brake/throttle inputs:
/// <list type="bullet">
///   <item><description>Entry — <c>Brake &gt; 0.1</c></description></item>
///   <item><description>Exit  — <c>Throttle &gt; 0.3</c></description></item>
///   <item><description>Mid   — everything else</description></item>
/// </list>
/// </remarks>
public static class CornerPhaseAnalyzer
{
    // ── Detection thresholds ──────────────────────────────────────────────────

    /// <summary>Minimum |AccGLateral| (G) to consider a sample as part of a corner.</summary>
    public const float LateralGThreshold = 0.40f;

    /// <summary>
    /// Consecutive samples above/below <see cref="LateralGThreshold"/> required to
    /// commit to a state transition (hysteresis). Suppresses false triggers from
    /// transient G spikes. At 250 Hz, 10 samples ≈ 40 ms.
    /// </summary>
    public const int   HysteresisSamples = 10;

    /// <summary>
    /// Minimum corner length (samples) to be included in the output.
    /// Filters out micro-events shorter than ~100 ms (25 samples at 250 Hz).
    /// </summary>
    public const int   MinCornerSamples  = 25;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the most recent <paramref name="windowSeconds"/> of data from
    /// <paramref name="buffer"/> and returns per-corner summaries, oldest first.
    /// Returns an empty array when the buffer contains no data within the window.
    /// </summary>
    /// <param name="buffer">Ring buffer populated by <see cref="AcTelemetryReader"/>.</param>
    /// <param name="windowSeconds">Time window to examine (e.g. 30.0 = last 30 s).</param>
    public static CornerSummary[] Analyze(TelemetryRingBuffer buffer, double windowSeconds)
    {
        if (buffer is null)     throw new ArgumentNullException(nameof(buffer));
        if (windowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(windowSeconds));

        // 260 Hz = 250 Hz nominal + 4 % safety margin; actual window trimmed by timestamp below
        var maxSamples = (int)(windowSeconds * 260) + 1;
        var temp       = new TelemetrySample[maxSamples];
        var total      = buffer.CopyTail(temp, maxSamples);
        if (total == 0) return [];

        var cutoff = temp[total - 1].Timestamp - TimeSpan.FromSeconds(windowSeconds);
        int start  = 0;
        while (start < total && temp[start].Timestamp < cutoff) start++;
        var count = total - start;
        return count == 0 ? [] : DetectCorners(temp, start, count);
    }

    /// <summary>
    /// Analyzes the first <paramref name="count"/> entries of <paramref name="samples"/>
    /// (oldest first) and returns per-corner summaries, oldest first.
    /// </summary>
    public static CornerSummary[] Analyze(TelemetrySample[] samples, int count)
    {
        if (samples is null) throw new ArgumentNullException(nameof(samples));
        if (count <= 0) return [];
        count = Math.Min(count, samples.Length);
        return DetectCorners(samples, 0, count);
    }

    // ── FormatLog ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns two (tag, message) pairs for the supplied <see cref="CornerSummary"/>,
    /// suitable for appending to an analysis terminal.
    /// </summary>
    public static (string Tag, string Msg)[] FormatLog(in CornerSummary cs)
    {
        var cornerTag = $"CURVA {cs.CornerIndex + 1}";
        var header    = $"Pos {cs.LapPos:P0}  Dur {cs.DurationMs / 1000.0:F1}s  Lat {cs.PeakLateralG:F1}G";

        var usMax = Math.Max(cs.UndersteerEntry, Math.Max(cs.UndersteerMid, cs.UndersteerExit));
        var osMax = Math.Max(cs.OversteerEntry,  cs.OversteerExit);

        string detailMsg;
        if (cs.LockupFront > 0.25f)
            detailMsg = $"Bloqueo del. {cs.LockupFront:P0}  —  revisar punto / presión de frenada";
        else if (cs.WheelspinRear > 0.25f)
            detailMsg = $"Patinamiento tra. {cs.WheelspinRear:P0}  —  diferencial / TCS / apertura de gas";
        else if (usMax > 0.10f)
            detailMsg = $"Subviraje — ent {cs.UndersteerEntry:P0}  med {cs.UndersteerMid:P0}  sal {cs.UndersteerExit:P0}";
        else if (osMax > 0.10f)
            detailMsg = $"Sobreviraje — ent {cs.OversteerEntry:P0}  sal {cs.OversteerExit:P0}";
        else
            detailMsg = "Balance neutro — sin anomalías detectadas en esta curva";

        return
        [
            (cornerTag,          header),
            ("→ " + cs.Dominant, detailMsg),
        ];
    }

    // ── Corner detection state machine ────────────────────────────────────────

    private static CornerSummary[] DetectCorners(TelemetrySample[] buf, int offset, int count)
    {
        var summaries   = new List<CornerSummary>();
        int cornerIndex = 0;

        bool inCorner       = false;
        int  cornerStartIdx = 0;
        int  hysteresis     = 0;

        for (int i = 0; i < count; i++)
        {
            var g = Math.Abs(buf[offset + i].AccGLateral);

            if (!inCorner)
            {
                if (g >= LateralGThreshold)
                {
                    if (++hysteresis >= HysteresisSamples)
                    {
                        inCorner       = true;
                        cornerStartIdx = i - HysteresisSamples + 1;
                        hysteresis     = 0;
                    }
                }
                else
                {
                    hysteresis = 0;
                }
            }
            else
            {
                if (g < LateralGThreshold)
                {
                    if (++hysteresis >= HysteresisSamples)
                    {
                        // Last confirmed in-corner sample is (i - HysteresisSamples)
                        var cornerEnd = i - HysteresisSamples;
                        var len       = cornerEnd - cornerStartIdx + 1;
                        if (len >= MinCornerSamples)
                        {
                            summaries.Add(BuildSummary(buf, offset + cornerStartIdx, len, cornerIndex));
                            cornerIndex++;
                        }
                        inCorner   = false;
                        hysteresis = 0;
                    }
                }
                else
                {
                    hysteresis = 0;
                }
            }
        }

        // Include a corner still active at the end of the window (partial corner)
        if (inCorner)
        {
            var len = count - cornerStartIdx;
            if (len >= MinCornerSamples)
            {
                summaries.Add(BuildSummary(buf, offset + cornerStartIdx, len, cornerIndex));
            }
        }

        return [.. summaries];
    }

    // ── Per-corner feature aggregation ────────────────────────────────────────

    private static CornerSummary BuildSummary(TelemetrySample[] buf, int start, int count, int index)
    {
        // Slip-angle accumulators per sub-phase (Entry / Mid / Exit)
        double sumFrontEntry = 0, sumRearEntry = 0; int nEntry = 0;
        double sumFrontMid   = 0, sumRearMid   = 0; int nMid   = 0;
        double sumFrontExit  = 0, sumRearExit  = 0; int nExit  = 0;

        // Wheel-slip accumulators (all samples and braking-only)
        double sumFrontWS = 0, sumRearWS = 0;
        double sumFrontWSBrake = 0, sumRearWSBrake = 0;
        int    nBraking = 0;

        float peakLatG = 0;
        int   apexIdx  = 0;

        for (int i = 0; i < count; i++)
        {
            ref readonly var s = ref buf[start + i];

            var frontSlip = (Math.Abs(s.SlipAngleFL) + Math.Abs(s.SlipAngleFR)) * 0.5;
            var rearSlip  = (Math.Abs(s.SlipAngleRL) + Math.Abs(s.SlipAngleRR)) * 0.5;

            // Sub-phase: Entry = braking, Exit = throttle, Mid = neutral
            if (s.Brake > 0.1f)
            {
                sumFrontEntry += frontSlip; sumRearEntry += rearSlip; nEntry++;
            }
            else if (s.Throttle > 0.3f)
            {
                sumFrontExit += frontSlip; sumRearExit += rearSlip; nExit++;
            }
            else
            {
                sumFrontMid += frontSlip; sumRearMid += rearSlip; nMid++;
            }

            var frontWS = (Math.Abs(s.WheelSlipFL) + Math.Abs(s.WheelSlipFR)) * 0.5;
            var rearWS  = (Math.Abs(s.WheelSlipRL) + Math.Abs(s.WheelSlipRR)) * 0.5;
            sumFrontWS += frontWS;
            sumRearWS  += rearWS;

            if (s.Brake > 0.1f)
            {
                sumFrontWSBrake += frontWS; sumRearWSBrake += rearWS; nBraking++;
            }

            var lat = Math.Abs(s.AccGLateral);
            if (lat > peakLatG) { peakLatG = (float)lat; apexIdx = i; }
        }

        // ── Normalized understeer / oversteer ─────────────────────────────────

        float usEntry = 0, osEntry = 0;
        if (nEntry > 0)
        {
            var d = sumFrontEntry / nEntry - sumRearEntry / nEntry;
            usEntry = Norm(Math.Max(0.0, d),  SlipThreshold);
            osEntry = Norm(Math.Max(0.0, -d), SlipThreshold);
        }

        var usMid = nMid > 0
            ? Norm(Math.Max(0.0, sumFrontMid / nMid - sumRearMid / nMid), SlipThreshold)
            : 0f;

        float usExit = 0, osExit = 0;
        if (nExit > 0)
        {
            var d = sumFrontExit / nExit - sumRearExit / nExit;
            usExit = Norm(Math.Max(0.0, d),  SlipThreshold);
            osExit = Norm(Math.Max(0.0, -d), SlipThreshold);
        }

        // ── Wheelspin / lockup ─────────────────────────────────────────────────

        var n      = (double)count;
        var avgFWS = sumFrontWS / n;
        var avgRWS = sumRearWS  / n;
        var wsRear = Norm(Math.Max(0.0, (avgFWS > Epsilon ? avgRWS / avgFWS : 1.0) - 1.0), 1.0);

        var lockFront = 0f;
        if (nBraking > 0)
        {
            var avgFB = sumFrontWSBrake / nBraking;
            var avgRB = sumRearWSBrake  / nBraking;
            var lr    = avgRB > Epsilon ? avgFB / avgRB : 1.0;
            lockFront = Norm(Math.Max(0.0, lr - 1.0), 1.0);
        }

        // ── Dominant issue ────────────────────────────────────────────────────

        var usMax = Math.Max(usEntry, Math.Max(usMid, usExit));
        var osMax = Math.Max(osEntry, osExit);

        string dominant;
        if      (lockFront >= wsRear && lockFront > 0.25f) dominant = "LOCK";
        else if (wsRear > lockFront  && wsRear    > 0.25f) dominant = "SPIN";
        else if (usMax >= osMax      && usMax     > 0.10f) dominant = "US";
        else if (osMax > usMax       && osMax     > 0.10f) dominant = "OS";
        else                                               dominant = "OK";

        // ── Timing / position ─────────────────────────────────────────────────

        var durationMs = (int)(buf[start + count - 1].Timestamp - buf[start].Timestamp)
                             .TotalMilliseconds;

        return new CornerSummary
        {
            CornerIndex     = index,
            StartTimestamp  = buf[start].Timestamp,
            LapPos          = buf[start + apexIdx].NormalizedLapPos,
            DurationMs      = Math.Max(0, durationMs),
            PeakLateralG    = peakLatG,
            UndersteerEntry = usEntry,
            UndersteerMid   = usMid,
            UndersteerExit  = usExit,
            OversteerEntry  = osEntry,
            OversteerExit   = osExit,
            WheelspinRear   = wsRear,
            LockupFront     = lockFront,
            Dominant        = dominant,
        };
    }

    private const double SlipThreshold = FeatureExtractor.SlipAngleThreshold; // 0.10 rad
    private const double Epsilon       = 1e-6;

    private static float Norm(double raw, double threshold)
        => (float)Math.Min(raw / threshold, 1.0);
}
