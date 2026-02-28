namespace AvoPerformanceSetupAI.Models;

/// <summary>
/// A single telemetry sample captured from Assetto Corsa's shared-memory API.
/// <para>
/// Wheel array index order matches the AC convention: FL = 0, FR = 1, RL = 2, RR = 3.
/// </para>
/// </summary>
public sealed class AcTelemetrySnapshot
{
    // ── Session ────────────────────────────────────────────────────────────────

    /// <summary><c>true</c> when AC is running an active (live) session.</summary>
    public bool  IsLive         { get; init; }

    /// <summary>0 = practice, 1 = qualify, 2 = race, 3 = hotlap.</summary>
    public int   SessionType    { get; init; }

    public int   CompletedLaps  { get; init; }

    /// <summary>Track surface grip from AC's graphics page. 1.0 = fully dry, lower = wet/dirty.</summary>
    public float SurfaceGrip    { get; init; }

    public bool  IsInPit        { get; init; }

    // ── Car state ──────────────────────────────────────────────────────────────

    public float SpeedKmh       { get; init; }

    /// <summary>Throttle position 0..1.</summary>
    public float Throttle       { get; init; }

    /// <summary>Brake position 0..1.</summary>
    public float Brake          { get; init; }

    /// <summary>Normalised steering angle –1 (full left) to +1 (full right).</summary>
    public float SteerAngle     { get; init; }

    /// <summary>0 = reverse, 1 = neutral, 2..8 = forward gears.</summary>
    public int   Gear           { get; init; }

    public int   Rpms           { get; init; }

    // ── G forces ──────────────────────────────────────────────────────────────

    public float AccGLateral    { get; init; }
    public float AccGLong       { get; init; }
    public float AccGVertical   { get; init; }

    // ── Per-wheel arrays (FL=0, FR=1, RL=2, RR=3) ─────────────────────────────

    public float[] WheelSlip        { get; init; } = new float[4];
    public float[] TyreCoreTemp     { get; init; } = new float[4];
    public float[] TyrePressure     { get; init; } = new float[4];
    public float[] SuspensionTravel { get; init; } = new float[4];
    public float[] TyreWear         { get; init; } = new float[4];

    // ── Car / track info (from static page) ───────────────────────────────────

    public string CarModel  { get; init; } = string.Empty;
    public string TrackName { get; init; } = string.Empty;
    public float  MaxRpm    { get; init; }
}
