using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using AvoPerformanceSetupAI.Models;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// Reads live telemetry data from Assetto Corsa's three Windows shared-memory pages:
/// <c>Local\acpmf_physics</c>, <c>Local\acpmf_graphics</c>, and <c>Local\acpmf_static</c>.
/// <para>
/// All byte offsets are derived from the official AC SDK <c>SharedFileOut.h</c>
/// with <c>Pack = 4</c> / MSVC default alignment.
/// </para>
/// <para>
/// Call <see cref="TryRead"/> at any interval from any thread.
/// Returns <c>null</c> when AC is not running or the shared memory is unavailable.
/// </para>
/// </summary>
public static class AcTelemetryReader
{
    // ── Shared memory map names ───────────────────────────────────────────────

    private const string PhysicsMapName  = "Local\\acpmf_physics";
    private const string GraphicsMapName = "Local\\acpmf_graphics";
    private const string StaticMapName   = "Local\\acpmf_static";

    // Maximum bytes to read per page (generous upper bounds that cover all AC versions)
    private const int PhysicsBufSize  = 1024;
    private const int GraphicsBufSize = 2048;
    private const int StaticBufSize   = 1024;

    // ── Physics page byte offsets ─────────────────────────────────────────────
    // Layout: SPageFilePhysics, Pack=4 (from AC SDK SharedFileOut.h)

    private const int P_GAS          = 4;    // float gas
    private const int P_BRAKE        = 8;    // float brake
    private const int P_GEAR         = 16;   // int gear
    private const int P_RPMS         = 20;   // int rpms
    private const int P_STEER        = 24;   // float steerAngle
    private const int P_SPEED        = 28;   // float speedKmh
    // velocity[3] at 32–40
    private const int P_ACCG_LAT     = 44;   // accG[0] – lateral
    private const int P_ACCG_LON     = 48;   // accG[1] – longitudinal
    private const int P_ACCG_VER     = 52;   // accG[2] – vertical
    // wheelSlip[4] at 56–68
    private const int P_SLIP_FL      = 56;
    private const int P_SLIP_FR      = 60;
    private const int P_SLIP_RL      = 64;
    private const int P_SLIP_RR      = 68;
    // wheelsPressure[4] at 88–100
    private const int P_PRES_FL      = 88;
    private const int P_PRES_FR      = 92;
    private const int P_PRES_RL      = 96;
    private const int P_PRES_RR      = 100;
    // tyreWear[4] at 120–132
    private const int P_WEAR_FL      = 120;
    private const int P_WEAR_FR      = 124;
    private const int P_WEAR_RL      = 128;
    private const int P_WEAR_RR      = 132;
    // tyreCoreTemperature[4] at 152–164
    private const int P_TEMP_FL      = 152;
    private const int P_TEMP_FR      = 156;
    private const int P_TEMP_RL      = 160;
    private const int P_TEMP_RR      = 164;
    // suspensionTravel[4] at 184–196
    private const int P_SUSP_FL      = 184;
    private const int P_SUSP_FR      = 188;
    private const int P_SUSP_RL      = 192;
    private const int P_SUSP_RR      = 196;

    // ── Graphics page byte offsets ────────────────────────────────────────────
    // Layout: SPageFileGraphics, Pack=4
    //   0   int packetId
    //   4   int status       (0=off, 1=replay, 2=live, 3=pause)
    //   8   int session      (0=practice, 1=qualify, 2=race, 3=hotlap)
    //  12   wchar_t currentTime[15]   30 bytes
    //  42   wchar_t lastTime[15]      30 bytes
    //  72   wchar_t bestTime[15]      30 bytes
    // 102   wchar_t split[15]         30 bytes
    // 132   int completedLaps
    // 136   int position
    // 140   int iCurrentTime
    // 144   int iLastTime
    // 148   int iBestTime
    // 152   float sessionTimeLeft
    // 156   float distanceTraveled
    // 160   bool isInPit             (1 byte + 3 padding)
    // 164   int currentSectorIndex
    // 168   int lastSectorTime
    // 172   int numberOfLaps
    // 176   wchar_t tyreCompound[33]  66 bytes + 2 padding → next at 244
    // 244   float replayTimeMultiplier
    // 248   float normalizedCarPosition
    // 252   float activeCars
    // 256   float carCoordinates[60][3]  720 bytes → next at 976
    // 976   int carID[60]             240 bytes → next at 1216
    // 1216  int playerCarID
    // 1220  float penaltyTime
    // 1224  int flag
    // 1228  int idealLineOn
    // 1232  bool isInPitLane          (1 byte + 3 padding)
    // 1236  float surfaceGrip

    private const int G_STATUS          = 4;
    private const int G_SESSION         = 8;
    private const int G_COMPLETED_LAPS  = 132;
    private const int G_IS_IN_PIT       = 160;   // bool, 1 byte
    private const int G_SURFACE_GRIP    = 1236;  // float

    // ── Static page byte offsets ──────────────────────────────────────────────
    // Layout: SPageFileStatic, Pack=4
    //   0   wchar_t smVersion[15]     30 bytes
    //  30   wchar_t acVersion[15]     30 bytes
    //  60   int numberOfSessions
    //  64   int numCars
    //  68   wchar_t carModel[33]      66 bytes + 2 padding → next at 136
    // 136   wchar_t track[33]         66 bytes + 2 padding → next at 204
    // 204   wchar_t playerName[33]    66 bytes + 2 padding → next at 272
    // 272   wchar_t playerSurname[33] 66 bytes + 2 padding → next at 340
    // 340   wchar_t playerNick[33]    66 bytes + 2 padding → next at 408
    // 408   int sectorCount
    // 412   float maxTorque
    // 416   float maxPower
    // 420   float maxRpm
    // 424   float maxFuel
    // 428   float suspensionMaxTravel[4]
    // 444   float tyreRadius[4]

    private const int S_CARMODEL = 68;    // wchar_t[33]
    private const int S_TRACK    = 136;   // wchar_t[33]
    private const int S_MAXRPM   = 420;   // float

    // ── Cached static data ────────────────────────────────────────────────────

    private static string   _carModel         = string.Empty;
    private static string   _trackName        = string.Empty;
    private static float    _maxRpm           = 8000f;
    private static DateTime _lastStaticRefresh = DateTime.MinValue;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to read a telemetry snapshot from Assetto Corsa's shared memory.
    /// Returns <c>null</c> when AC is not running, no session is active, or any I/O
    /// error occurs (e.g. the shared-memory pages haven't been created yet).
    /// </summary>
    public static AcTelemetrySnapshot? TryRead()
    {
        try
        {
            var physBuf = ReadMap(PhysicsMapName,  PhysicsBufSize);
            var grafBuf = ReadMap(GraphicsMapName, GraphicsBufSize);
            if (physBuf is null || grafBuf is null) return null;

            int  status  = ReadInt32(grafBuf, G_STATUS);
            bool isLive  = status == 2;   // AC_LIVE

            // Refresh car/track name at most once every 5 seconds
            TryRefreshStatic();

            float surfaceGrip = grafBuf.Length > G_SURFACE_GRIP + 4
                ? ReadFloat(grafBuf, G_SURFACE_GRIP)
                : 1.0f;

            return new AcTelemetrySnapshot
            {
                IsLive        = isLive,
                SessionType   = ReadInt32(grafBuf, G_SESSION),
                CompletedLaps = ReadInt32(grafBuf, G_COMPLETED_LAPS),
                IsInPit       = grafBuf[G_IS_IN_PIT] != 0,
                SurfaceGrip   = Math.Clamp(surfaceGrip, 0f, 1f),

                SpeedKmh  = ReadFloat(physBuf, P_SPEED),
                Throttle  = Math.Clamp(ReadFloat(physBuf, P_GAS),   0f, 1f),
                Brake     = Math.Clamp(ReadFloat(physBuf, P_BRAKE),  0f, 1f),
                SteerAngle= ReadFloat(physBuf, P_STEER),
                Gear      = ReadInt32(physBuf, P_GEAR),
                Rpms      = ReadInt32(physBuf, P_RPMS),

                AccGLateral  = ReadFloat(physBuf, P_ACCG_LAT),
                AccGLong     = ReadFloat(physBuf, P_ACCG_LON),
                AccGVertical = ReadFloat(physBuf, P_ACCG_VER),

                WheelSlip = new[]
                {
                    ReadFloat(physBuf, P_SLIP_FL), ReadFloat(physBuf, P_SLIP_FR),
                    ReadFloat(physBuf, P_SLIP_RL), ReadFloat(physBuf, P_SLIP_RR),
                },
                TyreCoreTemp = new[]
                {
                    ReadFloat(physBuf, P_TEMP_FL), ReadFloat(physBuf, P_TEMP_FR),
                    ReadFloat(physBuf, P_TEMP_RL), ReadFloat(physBuf, P_TEMP_RR),
                },
                TyrePressure = new[]
                {
                    ReadFloat(physBuf, P_PRES_FL), ReadFloat(physBuf, P_PRES_FR),
                    ReadFloat(physBuf, P_PRES_RL), ReadFloat(physBuf, P_PRES_RR),
                },
                SuspensionTravel = new[]
                {
                    ReadFloat(physBuf, P_SUSP_FL), ReadFloat(physBuf, P_SUSP_FR),
                    ReadFloat(physBuf, P_SUSP_RL), ReadFloat(physBuf, P_SUSP_RR),
                },
                TyreWear = new[]
                {
                    ReadFloat(physBuf, P_WEAR_FL), ReadFloat(physBuf, P_WEAR_FR),
                    ReadFloat(physBuf, P_WEAR_RL), ReadFloat(physBuf, P_WEAR_RR),
                },

                CarModel  = _carModel,
                TrackName = _trackName,
                MaxRpm    = _maxRpm,
            };
        }
        catch
        {
            // AC not running, map not yet created, or version mismatch — return null silently
            return null;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void TryRefreshStatic()
    {
        if ((DateTime.UtcNow - _lastStaticRefresh).TotalSeconds < 5) return;
        _lastStaticRefresh = DateTime.UtcNow;

        try
        {
            var statBuf = ReadMap(StaticMapName, StaticBufSize);
            if (statBuf is null) return;

            _carModel  = ReadWString(statBuf, S_CARMODEL, 33);
            _trackName = ReadWString(statBuf, S_TRACK,    33);
            if (statBuf.Length >= S_MAXRPM + 4)
            {
                float rpm = ReadFloat(statBuf, S_MAXRPM);
                if (rpm > 0) _maxRpm = rpm;
            }
        }
        catch { /* ignore – static page is optional */ }
    }

    /// <summary>
    /// Opens a named memory-mapped file and reads up to <paramref name="maxBytes"/> bytes.
    /// Returns <c>null</c> if the file doesn't exist (AC not running) or access is denied.
    /// </summary>
    private static byte[]? ReadMap(string mapName, int maxBytes)
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(
                mapName, MemoryMappedFileRights.Read);
            using var acc = mmf.CreateViewAccessor(
                0, 0, MemoryMappedFileAccess.Read);

            int size = (int)Math.Min(maxBytes, acc.Capacity);
            var buf  = new byte[size];
            acc.ReadArray(0, buf, 0, size);
            return buf;
        }
        catch (FileNotFoundException)        { return null; }
        catch (UnauthorizedAccessException)  { return null; }
        catch (IOException)                  { return null; }
    }

    private static float  ReadFloat(byte[] buf, int offset) =>
        BitConverter.ToSingle(buf, offset);

    private static int    ReadInt32(byte[] buf, int offset) =>
        BitConverter.ToInt32(buf, offset);

    /// <summary>Reads a null-terminated UTF-16 string from a byte buffer.</summary>
    private static string ReadWString(byte[] buf, int offset, int maxChars)
    {
        int maxBytes  = maxChars * 2;
        int available = Math.Min(maxBytes, buf.Length - offset);
        if (available <= 0) return string.Empty;

        // Find null terminator: require at least 2 bytes to read a UTF-16 code unit safely.
        int end = 0;
        while (end + 2 <= available &&
               (buf[offset + end] != 0 || buf[offset + end + 1] != 0))
            end += 2;

        return Encoding.Unicode.GetString(buf, offset, end);
    }
}
