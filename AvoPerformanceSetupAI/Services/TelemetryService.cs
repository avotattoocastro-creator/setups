using System;
using System.Threading;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.Telemetry;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// Manages the active telemetry data source — either Assetto Corsa shared memory
/// or the built-in simulation generator.  Auto-retries the AC connection every 2 s
/// when the game is not yet running.
/// </summary>
/// <remarks>
/// All public methods are thread-safe.  <see cref="ConnectionChanged"/> is raised on
/// a background thread; consumers must marshal to the UI thread before touching UI
/// elements (e.g. via <c>DispatcherQueue.TryEnqueue</c>).
/// </remarks>
public sealed class TelemetryService : IDisposable
{
    private readonly AcTelemetryReader _acReader = new();

    // ── Thread-safety ─────────────────────────────────────────────────────────

    private readonly object _lock = new();
    private Timer? _retryTimer;
    private bool _active; // set false in Stop() so in-flight retries don't fire events

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>Ring buffer populated by the AC reader (empty while simulating).</summary>
    public TelemetryRingBuffer Buffer => _acReader.Buffer;

    /// <summary>
    /// <see langword="true"/> when AC shared memory is open and the 250 Hz poll
    /// loop is running.
    /// </summary>
    public bool IsAcConnected => _acReader.IsConnected;

    /// <summary>
    /// Raised on a background thread whenever connection state or status text changes.
    /// Parameters: (<c>isConnected</c>, <c>statusText</c>).
    /// </summary>
    public event Action<bool, string>? ConnectionChanged;

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the requested telemetry source.
    /// <list type="bullet">
    ///   <item><see cref="TelemetrySource.AssettoCorsa"/> — attempts a shared-memory
    ///   connect; if AC is not running, fires <see cref="ConnectionChanged"/> with
    ///   <c>"AC not running"</c> and starts a 2-second retry timer.</item>
    ///   <item><see cref="TelemetrySource.Simulation"/> — stops the AC reader and
    ///   fires <see cref="ConnectionChanged"/> with <c>"Simulation"</c> so the
    ///   ViewModel's own simulation timer drives all data.</item>
    /// </list>
    /// </summary>
    public void Start(TelemetrySource source)
    {
        // Cancel any in-flight retry timer before starting a new source.
        Timer? old;
        lock (_lock)
        {
            _active    = true;
            old        = _retryTimer;
            _retryTimer = null;
        }
        old?.Dispose();
        _acReader.Disconnect();

        if (source == TelemetrySource.AssettoCorsa)
        {
            if (TryAutoConnectAc())
            {
                ConnectionChanged?.Invoke(true, "Connected to AC");
            }
            else
            {
                ConnectionChanged?.Invoke(false, "AC not running");
                lock (_lock)
                {
                    if (_active) // guard against an immediate Stop() call
                        _retryTimer = new Timer(
                            _ => RetryConnect(),
                            null,
                            TimeSpan.FromSeconds(2),
                            TimeSpan.FromSeconds(2));
                }
            }
        }
        else
        {
            // Simulation: the ViewModel's simulation timer drives all channel data.
            ConnectionChanged?.Invoke(false, "Simulation");
        }
    }

    /// <summary>Stops the active source and cancels any pending retry timer.</summary>
    public void Stop()
    {
        Timer? old;
        lock (_lock)
        {
            _active    = false;
            old        = _retryTimer;
            _retryTimer = null;
        }
        old?.Dispose(); // dispose outside the lock so the callback can finish cleanly
        _acReader.Disconnect();
    }

    /// <summary>
    /// Attempts to connect to Assetto Corsa shared memory once.
    /// Returns <see langword="true"/> on success.
    /// </summary>
    public bool TryAutoConnectAc() => _acReader.TryConnect();

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose() => Stop();

    // ── Private helpers ───────────────────────────────────────────────────────

    private void RetryConnect()
    {
        if (!TryAutoConnectAc()) return;

        Timer? old;
        lock (_lock)
        {
            // If Stop() was called while we were connecting, don't surface the event.
            if (!_active) return;
            old        = _retryTimer;
            _retryTimer = null;
        }
        old?.Dispose();
        ConnectionChanged?.Invoke(true, "Connected to AC");
    }
}
