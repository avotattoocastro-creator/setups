using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.Services;
using AvoPerformanceSetupAI.Telemetry;

namespace AvoPerformanceSetupAI.ViewModels;

public partial class TelemetryViewModel : ObservableObject, IDisposable
{
    private DispatcherQueue? _dispatcher;
    private System.Threading.Timer? _updateTimer;
    private int  _tick;
    private readonly Random _rng = new(Environment.TickCount);

    // Stored so Initialize can subscribe and Dispose can unsubscribe (prevents memory leaks).
    private Action<bool, string>? _connectionChangedHandler;
    private Action?               _suggestSwitchHandler;

    // ── Telemetry service (AC shared memory + simulation routing) ─────────────

    private readonly TelemetryService _telemetryService = new();

    // ── Corner detector (stateful, thread-safe) ───────────────────────────────

    private readonly CornerDetector _cornerDetector = new();

    /// <summary>50 ms DispatcherTimer for real-time phase/corner updates on the UI thread.</summary>
    private DispatcherTimer? _fastTimer;

    /// <summary>CornerIndex of the last corner already reflected in the bindable properties.</summary>
    private int _lastReportedCornerIndex = -1;

    /// <summary>Number of 800 ms ticks between feature-analysis runs (6 × 800 ms ≈ 4.8 s).</summary>
    private const int FeatureAnalysisTickInterval = 6;

    /// <summary>Number of 800 ms ticks between corner-analysis runs (15 × 800 ms ≈ 12 s).</summary>
    private const int CornerAnalysisTickInterval  = 15;

    // ── Observable state ─────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isSimulating;
    [ObservableProperty] private string _statusText            = "● DETENIDO";
    [ObservableProperty] private string _lapTimeReal           = "--:--.---";
    [ObservableProperty] private string _lapTimeIdeal          = "1:52.847";
    [ObservableProperty] private string _lapDelta              = "---";
    [ObservableProperty] private double _lapPosition;
    [ObservableProperty] private string _lapPositionText       = "Pos:  0%";
    [ObservableProperty] private bool   _isAcConnected;
    [ObservableProperty] private string _connectionStatusText  = "Simulation";
    [ObservableProperty] private TelemetrySource _selectedSource = TelemetrySource.Simulation;

    /// <summary>
    /// <see langword="true"/> when the service has exhausted
    /// <c>MaxRetriesBeforeSuggest</c> reconnect attempts and asks the user
    /// whether to switch to Simulation mode.
    /// </summary>
    [ObservableProperty] private bool _showSwitchToSimulationPrompt;

    /// <summary>
    /// Convenience bool for binding a XAML ToggleSwitch:
    /// <see langword="true"/> when <see cref="SelectedSource"/> is
    /// <see cref="TelemetrySource.AssettoCorsa"/>.
    /// </summary>
    public bool IsAcSourceSelected
    {
        get => SelectedSource == TelemetrySource.AssettoCorsa;
        set
        {
            // Ignore deselect attempts — the user must click the other button to switch.
            if (!value) return;
            SelectedSource = TelemetrySource.AssettoCorsa;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Convenience bool for binding a segmented ToggleButton:
    /// <see langword="true"/> when <see cref="SelectedSource"/> is
    /// <see cref="TelemetrySource.Simulation"/>.
    /// </summary>
    public bool IsSimulationSelected
    {
        get => SelectedSource == TelemetrySource.Simulation;
        set
        {
            // Ignore deselect attempts — the user must click the other button to switch.
            if (!value) return;
            SelectedSource = TelemetrySource.Simulation;
            OnPropertyChanged();
        }
    }

    partial void OnSelectedSourceChanged(TelemetrySource value)
    {
        OnPropertyChanged(nameof(IsAcSourceSelected));
        OnPropertyChanged(nameof(IsSimulationSelected));
    }

    // ── Corner / phase observables ────────────────────────────────────────────

    /// <summary>Current driving phase inferred from the latest telemetry sample.</summary>
    [ObservableProperty] private string _currentPhase = "—";

    /// <summary>Direction of the most recently completed corner ("Left", "Right", or "—").</summary>
    [ObservableProperty] private string _lastCornerDirection = "—";

    /// <summary>Understeer index (0..1) during the entry (braking) phase of the last corner.</summary>
    [ObservableProperty] private float _lastCornerUndersteerEntry;

    /// <summary>Understeer index (0..1) during the mid-corner phase of the last corner.</summary>
    [ObservableProperty] private float _lastCornerUndersteerMid;

    /// <summary>Understeer index (0..1) during the exit (acceleration) phase of the last corner.</summary>
    [ObservableProperty] private float _lastCornerUndersteerExit;

    /// <summary>Oversteer index (0..1) during the entry (braking) phase of the last corner.</summary>
    [ObservableProperty] private float _lastCornerOversteerEntry;

    /// <summary>Oversteer index (0..1) during the exit (acceleration) phase of the last corner.</summary>
    [ObservableProperty] private float _lastCornerOversteerExit;

    /// <summary>Duration of the most recently completed corner as a formatted string, e.g. "3.2s".</summary>
    [ObservableProperty] private string _lastCornerDurationText = "—";

    // ── Collections ──────────────────────────────────────────────────────────

    /// <summary>Telemetry channels — each holds both the real (live) and ideal (target) value.</summary>
    public ObservableCollection<TelemetryChannel> Channels    { get; } = new();

    /// <summary>
    /// Live rule-based proposals from the latest completed corner, combining
    /// Entry (braking), Mid (balance), and Exit (traction) phase evaluations.
    /// Suitable for binding to a real-time proposals panel.
    /// </summary>
    public ObservableCollection<Proposal> Proposals { get; } = new();

    /// <summary>Car-behaviour analysis log.</summary>
    public ObservableCollection<AnalysisEntry>    BehaviorLogs { get; } = new();

    /// <summary>Driving-time analysis log.</summary>
    public ObservableCollection<AnalysisEntry>    DrivingLogs  { get; } = new();

    /// <summary>Setup-improvement steps log.</summary>
    public ObservableCollection<AnalysisEntry>    SetupLogs    { get; } = new();

    /// <summary>Per-corner phase analysis log.</summary>
    public ObservableCollection<AnalysisEntry>    CornerLogs   { get; } = new();

    /// <summary>Tracks the newest corner timestamp already written to <see cref="CornerLogs"/> to avoid duplicates.</summary>
    private DateTime _lastCornerTimestamp = DateTime.MinValue;

    // ── Channel definitions (name, unit, initial ideal) ──────────────────────
    // Order must match LapProfiles below (index 0..12).

    private static readonly (string Name, string Unit, double Ideal)[] ChannelDefs =
    [
        ("SPEED",    "km/h", 230.0),
        ("RPM",      "rpm",  7100.0),
        ("GEAR",     "",       5.0),
        ("THROTTLE", "%",     98.0),
        ("BRAKE",    "%",      0.0),
        ("STEER",    "°",      1.0),
        ("LAT_G",    "G",      0.1),
        ("LONG_G",   "G",      0.3),
        ("FUEL",     "L",     43.5),
        ("T_F",      "°C",   82.0),
        ("T_R",      "°C",   87.0),
        ("P_F",      "bar",   1.80),
        ("P_R",      "bar",   1.72),
    ];

    // ── Lap-position profiles (pos 0..1 → ideal value) ───────────────────────
    // Eight waypoints model a generic racing circuit:
    //   0.00 = start/finish (exit of last corner, full throttle)
    //   0.15 = heavy braking zone (T1)
    //   0.25 = slow-corner apex
    //   0.38 = acceleration out of slow corner
    //   0.52 = high-speed straight (mid-lap)
    //   0.65 = medium braking zone (T2)
    //   0.78 = fast sweeper
    //   0.88 = final chicane
    //   1.00 = back at start/finish (same as 0.00)
    // Index order must match ChannelDefs exactly.

    private static readonly (double Pos, double Ideal)[][] LapProfiles =
    [
        // 0 SPEED km/h
        [(0.00,230),(0.15, 85),(0.25, 62),(0.38,138),(0.52,248),(0.65,172),(0.78,200),(0.88,128),(1.00,230)],
        // 1 RPM
        [(0.00,7100),(0.15,3400),(0.25,3000),(0.38,5500),(0.52,7400),(0.65,5000),(0.78,6800),(0.88,4800),(1.00,7100)],
        // 2 GEAR
        [(0.00,5),(0.15,2),(0.25,2),(0.38,3),(0.52,6),(0.65,4),(0.78,5),(0.88,3),(1.00,5)],
        // 3 THROTTLE %
        [(0.00,98),(0.15,0),(0.25,22),(0.38,100),(0.52,97),(0.65,25),(0.78,82),(0.88,35),(1.00,98)],
        // 4 BRAKE %
        [(0.00,0),(0.15,88),(0.25,8),(0.38,0),(0.52,0),(0.65,72),(0.78,0),(0.88,62),(1.00,0)],
        // 5 STEER °
        [(0.00,1),(0.15,3),(0.25,14),(0.38,7),(0.52,2),(0.65,6),(0.78,11),(0.88,9),(1.00,1)],
        // 6 LAT_G G
        [(0.00,0.1),(0.15,0.4),(0.25,1.8),(0.38,0.9),(0.52,0.2),(0.65,1.3),(0.78,2.1),(0.88,1.6),(1.00,0.1)],
        // 7 LONG_G G
        [(0.00,0.3),(0.15,-2.4),(0.25,-0.3),(0.38,0.9),(0.52,0.2),(0.65,-2.0),(0.78,0.1),(0.88,-1.8),(1.00,0.3)],
        // 8 FUEL L — decreases through the lap
        [(0.00,43.5),(0.50,43.3),(1.00,43.1)],
        // 9 T_F °C — mild variation
        [(0.00,82),(0.25,85),(0.52,84),(0.78,83),(1.00,82)],
        // 10 T_R °C
        [(0.00,87),(0.25,91),(0.52,88),(0.78,90),(1.00,87)],
        // 11 P_F bar
        [(0.00,1.80),(0.25,1.82),(0.52,1.81),(0.78,1.79),(1.00,1.80)],
        // 12 P_R bar
        [(0.00,1.72),(0.25,1.74),(0.52,1.73),(0.78,1.71),(1.00,1.72)],
    ];

    // ── Ticks per simulated lap ───────────────────────────────────────────────

    private const int LapTicks = 50; // 50 × 800 ms ≈ 40 s simulated lap

    // ── Analysis message banks ────────────────────────────────────────────────

    private static readonly (string Tag, string Msg)[] BehaviorMsgs =
    [
        ("UNDERSTEER", "Subviraje leve en curvas rápidas — reducir spoiler delantero"),
        ("BALANCE",    "Balance freno/aceleración dentro de rango óptimo"),
        ("TEMP_R",     "Temperatura trasera superior a delantera (+5 °C) — revisar camber"),
        ("OVERSTEER",  "Sobreviraje detectado en curvas lentas — aumentar ARB trasero"),
        ("GRIP",       "Pérdida de grip trasero en frenada — verificar presión neumáticos"),
        ("BALANCE",    "Distribución de peso lateral equilibrada — dentro de límites"),
        ("TIRE_WEAR",  "Desgaste asimétrico en neumático delantero izquierdo detectado"),
        ("AERO",       "Eficiencia aerodinámica dentro de parámetros nominales"),
        ("OVERSTEER",  "Rotación excesiva en entrada de curva — reducir diferencial"),
        ("TEMP_F",     "Temperatura delantera óptima — rango 80-86 °C mantenido"),
    ];

    private static readonly (string Tag, string Msg)[] DrivingMsgs =
    [
        ("LAP+0.3",   "Vuelta actual +0.312 s sobre referencia ideal"),
        ("SECTOR 1",  "S1: +0.05 s — frenada tardía en curva 3"),
        ("SECTOR 2",  "S2: -0.02 s — línea de apexe óptima"),
        ("BRAKE",     "Punto de frenada consistente en 12/15 curvas (80 %)"),
        ("THROTTLE",  "Acelerador agresivo en S2 — riesgo de spin en curva 8"),
        ("LAP-0.1",   "Vuelta anterior -0.098 s — mejora confirmada en S3"),
        ("CONSIST",   "Consistencia: 87 % — margen de mejora en S1"),
        ("SPEED",     "Velocidad punta 241 km/h / ideal 245 km/h (−4 km/h)"),
        ("BRAKE",     "Distancia de frenada 5 m mayor que referencia en T1"),
        ("SECTOR 3",  "S3: +0.08 s — salida de última chicane subóptima"),
    ];

    // ── Structured setup steps — each carries the log text and an optional proposal ────

    private static readonly (string Tag, string Msg, Proposal? Proposal)[] SetupSteps =
    [
        ("PASO 1",    "Reducir P_F 1.85 → 1.80 bar para mejorar grip frontal",
            new Proposal { Section="TYRES",       Parameter="PRESSURE_LF",    From="1.85", To="1.80", Delta="-0.05" }),
        ("PASO 2",    "Ajustar camber delantero −0.2° para equilibrar desgaste",
            new Proposal { Section="ALIGNMENT",   Parameter="CAMBER_LF",      From="-2.8", To="-3.0", Delta="-0.2"  }),
        ("PASO 3",    "Aumentar ARB trasero 1 click para reducir sobreviraje",
            new Proposal { Section="ARB",         Parameter="REAR",           From="3",    To="4",    Delta="+1"    }),
        ("PROPUESTA", "Propuesta #3 lista — delta estimado: −0.18 s/vuelta",          null),
        ("PASO 4",    "Reducir spoiler delantero 2 mm — mayor velocidad punta",
            new Proposal { Section="AERO",        Parameter="FRONT_WING",     From="8",    To="6",    Delta="-2"    }),
        ("VERIFICAR", "Comprobar temperatura neumáticos tras aplicar setup",           null),
        ("PASO 5",    "Incrementar bump trasero 1 click — mejora estabilidad",
            new Proposal { Section="DAMPERS",     Parameter="BUMP_REAR",      From="4",    To="5",    Delta="+1"    }),
        ("ÓPTIMO",    "Setup óptimo estimado para condición actual: pista seca",       null),
        ("PASO 6",    "Ajustar diff aceleración +2 para mejor tracción en salidas",
            new Proposal { Section="ELECTRONICS", Parameter="DIFF_ACC",       From="50",   To="52",   Delta="+2"    }),
        ("PROPUESTA", "Propuesta #4 — reducir ride height trasero 2 mm",
            new Proposal { Section="SUSPENSION",  Parameter="ROD_LENGTH_RR",  From="10",   To="8",    Delta="-2"    }),
    ];

    // ── Constructor ───────────────────────────────────────────────────────────

    public TelemetryViewModel()
    {
        var rngSeed = new Random(42);
        foreach (var (name, unit, ideal) in ChannelDefs)
        {
            Channels.Add(new TelemetryChannel
            {
                Name       = name,
                Unit       = unit,
                IdealValue = ideal,
                RealValue  = Math.Round(ideal * (0.92 + rngSeed.NextDouble() * 0.06), 2),
            });
        }

        // Seed initial entries so the terminals are not empty on first open
        Append(BehaviorLogs, "INICIO",  "Módulo de análisis de comportamiento listo.");
        Append(BehaviorLogs, "INFO",    "Pulse ▶ Iniciar para activar la telemetría en tiempo real.");
        Append(DrivingLogs,  "INICIO",  "Analizador de tiempo de conducción activo.");
        Append(DrivingLogs,  "INFO",    "Los datos de vuelta aparecerán al iniciar la simulación.");
        Append(SetupLogs,    "INICIO",  "Motor de propuestas de setup inicializado.");
        Append(SetupLogs,    "INFO",    "Los pasos de mejora se generarán automáticamente.");
        Append(CornerLogs,   "INICIO",  "Analizador de fases de curva activo.");
        Append(CornerLogs,   "INFO",    "Los resúmenes de curva aparecerán al conectar Assetto Corsa.");
    }

    /// <summary>
    /// Must be called once from the UI thread (page constructor) to allow the timer
    /// callbacks to marshal back onto the UI dispatcher.
    /// </summary>
    public void Initialize(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;

        // Wire TelemetryService.ConnectionChanged so background retries update
        // the UI-bound properties on the UI thread.
        _connectionChangedHandler = (isConnected, statusText) =>
            _dispatcher.TryEnqueue(() =>
            {
                IsAcConnected        = isConnected;
                ConnectionStatusText = statusText;
                AppLogger.Instance.Info(statusText);
            });
        _telemetryService.ConnectionChanged += _connectionChangedHandler;

        // Wire SuggestSwitchToSimulation: after 10 failed reconnect attempts the
        // service asks the user whether to fall back to simulation mode.
        _suggestSwitchHandler = () =>
            _dispatcher.TryEnqueue(() =>
            {
                ShowSwitchToSimulationPrompt = true;
                AppLogger.Instance.Warn(
                    "AC no responde tras 10 intentos — se sugiere cambiar a Simulación.");
            });
        _telemetryService.SuggestSwitchToSimulation += _suggestSwitchHandler;

        // Create a 50 ms DispatcherTimer for real-time phase/corner updates.
        // Must be created on the UI thread (DispatcherTimer fires on the thread it was created on).
        _fastTimer          = new DispatcherTimer();
        _fastTimer.Interval = TimeSpan.FromMilliseconds(50);
        _fastTimer.Tick    += OnFastTick;
    }

    /// <summary>Unsubscribes from <see cref="TelemetryService"/> events and releases resources.</summary>
    public void Dispose()
    {
        if (_connectionChangedHandler is not null)
        {
            _telemetryService.ConnectionChanged -= _connectionChangedHandler;
            _connectionChangedHandler = null;
        }
        if (_suggestSwitchHandler is not null)
        {
            _telemetryService.SuggestSwitchToSimulation -= _suggestSwitchHandler;
            _suggestSwitchHandler = null;
        }
        _telemetryService.Dispose();
        _updateTimer?.Dispose();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartSimulation()
    {
        IsSimulating = true;
        StatusText   = "● ACTIVO";
        _tick        = 0;

        // Delegate source management to TelemetryService.
        // ConnectionChanged will update IsAcConnected + ConnectionStatusText asynchronously.
        _telemetryService.Start(SelectedSource);
        AppLogger.Instance.Ai("Telemetría en tiempo real ACTIVADA — tick cada 800 ms.");

        _updateTimer = new System.Threading.Timer(OnTick, null, 0, 800);
        _fastTimer?.Start();
        StartSimulationCommand.NotifyCanExecuteChanged();
        StopSimulationCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() => !IsSimulating;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopSimulation()
    {
        IsSimulating = false;
        StatusText   = "● DETENIDO";
        _updateTimer?.Dispose();
        _updateTimer = null;
        _fastTimer?.Stop();
        _telemetryService.Stop();
        IsAcConnected                = false;
        ConnectionStatusText         = "Simulation";
        ShowSwitchToSimulationPrompt = false;
        CurrentPhase  = "—";
        AppLogger.Instance.Info("Telemetría pausada.");
        StartSimulationCommand.NotifyCanExecuteChanged();
        StopSimulationCommand.NotifyCanExecuteChanged();
    }

    private bool CanStop() => IsSimulating;

    [RelayCommand]
    private void ClearAnalysis()
    {
        BehaviorLogs.Clear();
        DrivingLogs.Clear();
        SetupLogs.Clear();
        CornerLogs.Clear();
        Proposals.Clear();
        _lastReportedCornerIndex  = -1;
        CurrentPhase              = "—";
        LastCornerDirection       = "—";
        LastCornerUndersteerEntry = 0f;
        LastCornerUndersteerMid   = 0f;
        LastCornerUndersteerExit  = 0f;
        LastCornerOversteerEntry  = 0f;
        LastCornerOversteerExit   = 0f;
        LastCornerDurationText    = "—";
        // AcTelemetryReader stamps every sample with DateTime.UtcNow, so using
        // DateTime.UtcNow here guarantees only corners whose first sample arrives
        // after the clear will be logged — no stale corners re-appear.
        _lastCornerTimestamp = DateTime.UtcNow;
        AppLogger.Instance.Info("Paneles de análisis de telemetría limpiados.");
    }

    /// <summary>
    /// Accepts the "switch to Simulation?" suggestion: changes the source, dismisses the
    /// prompt, and re-starts telemetry in Simulation mode so the session continues
    /// without interruption.
    /// </summary>
    [RelayCommand]
    private void ConfirmSwitchToSimulation()
    {
        ShowSwitchToSimulationPrompt = false;
        SelectedSource = TelemetrySource.Simulation;
        if (IsSimulating)
        {
            _telemetryService.Start(TelemetrySource.Simulation);
            // ConnectionChanged will update status text asynchronously.
        }
        AppLogger.Instance.Info("Fuente cambiada a Simulación por solicitud del usuario.");
    }

    /// <summary>Dismisses the "switch to Simulation?" prompt without changing the source.</summary>
    [RelayCommand]
    private void DismissSwitchToSimulation()
    {
        ShowSwitchToSimulationPrompt = false;
        AppLogger.Instance.Info("Sugerencia de cambio a Simulación descartada — continuando reconexión.");
    }

    // ── Simulation tick ───────────────────────────────────────────────────────

    private void OnTick(object? state)
    {
        var t = ++_tick;

        _dispatcher?.TryEnqueue(() =>
        {
            UpdateChannels();

            if (t % 4  == 0) UpdateLapTime();
            if (t % 5  == 0) Append(BehaviorLogs, BehaviorMsgs[(t / 5)  % BehaviorMsgs.Length]);
            if (t % 7  == 0) Append(DrivingLogs,  DrivingMsgs [(t / 7)  % DrivingMsgs.Length]);
            if (t % 11 == 0)
            {
                var step = SetupSteps[(t / 11) % SetupSteps.Length];
                Append(SetupLogs, step.Tag, step.Msg);
                if (step.Proposal is not null)
                    SessionsViewModel.Shared.PushTelemetryProposal(step.Proposal);
            }

            // ── Feature analysis from AC ring buffer ──────────────────────────
            if (IsAcConnected && t % FeatureAnalysisTickInterval == 0)
                RunFeatureAnalysis();
            if (IsAcConnected && t % CornerAnalysisTickInterval == 0)
                RunCornerAnalysis();
        });
    }

    /// <summary>
    /// Extracts features from the most recent 2 seconds of samples in the ring buffer,
    /// appends human-readable results to the behaviour log, and pushes any rule-based
    /// setup proposals to <see cref="SessionsViewModel.Shared"/>.
    /// </summary>
    private void RunFeatureAnalysis()
    {
        var frame = FeatureExtractor.ExtractFrame(_telemetryService.Buffer, windowSeconds: 2.0);
        if (frame.SampleCount == 0) return;

        foreach (var (tag, msg) in FeatureExtractor.FormatLog(in frame))
            Append(BehaviorLogs, tag, msg);

        // Push rule-based proposals so they appear in the Sessions panel
        var proposals = RuleEngine.Evaluate(in frame);
        if (proposals.Length > 0)
            SessionsViewModel.Shared.PushTelemetryProposals(proposals);
    }

    /// <summary>
    /// Analyzes the most recent 30 seconds of samples for corner events and
    /// appends new per-corner summaries to <see cref="CornerLogs"/>.
    /// Only corners whose <see cref="CornerSummary.StartTimestamp"/> is newer
    /// than the last logged timestamp are added (prevents duplicates across calls).
    /// </summary>
    private void RunCornerAnalysis()
    {
        var corners = CornerPhaseAnalyzer.Analyze(_telemetryService.Buffer, windowSeconds: 30.0);
        foreach (var cs in corners)
        {
            if (cs.StartTime <= _lastCornerTimestamp) continue;
            foreach (var (tag, msg) in CornerPhaseAnalyzer.FormatLog(in cs))
                Append(CornerLogs, tag, msg);
        }
        if (corners.Length > 0)
            _lastCornerTimestamp = corners[^1].StartTime;
    }

    private void UpdateChannels()
    {
        // ── When AC is connected and in a live session, use real sample data ──
        if (IsAcConnected)
        {
            var sample = _telemetryService.Buffer.ReadLast();
            if (sample.AcStatus == (int)AcStatus.Live && sample.SpeedKmh >= 0)
            {
                UpdateChannelsFromSample(in sample);
                return;
            }
        }

        // ── Simulation fallback ───────────────────────────────────────────────

        // Advance lap position (cycles 0 → 1 over LapTicks ticks)
        LapPosition     = (_tick % LapTicks) / (double)LapTicks;
        LapPositionText = $"Pos: {LapPosition * 100,3:F0}%";

        for (int i = 0; i < Channels.Count && i < LapProfiles.Length; i++)
        {
            var ch    = Channels[i];
            var ideal = LerpProfile(LapProfiles[i], LapPosition);
            ch.IdealValue = Math.Round(ideal, 2);

            // Oscillate real value ±4 % around the position-adjusted ideal
            var noise = (_rng.NextDouble() - 0.5) * 0.08;
            ch.RealValue = Math.Round(ideal * (1.0 + noise), 2);
        }
    }

    /// <summary>
    /// Updates all channels from a real AC <see cref="TelemetrySample"/>.
    /// Ideal values still come from the <see cref="LapProfiles"/> interpolation,
    /// keyed on the actual normalised lap position reported by AC.
    /// Real values map directly from the sample fields.
    /// </summary>
    private void UpdateChannelsFromSample(in TelemetrySample s)
    {
        // Lap position from AC spline
        LapPosition     = Math.Clamp(s.NormalizedLapPos, 0.0, 1.0);
        LapPositionText = $"Pos: {LapPosition * 100,3:F0}%";

        // Prepare real values for each named channel
        // Index order must match ChannelDefs (SPEED, RPM, GEAR, THROTTLE, BRAKE,
        //   STEER, LAT_G, LONG_G, FUEL, T_F, T_R, P_F, P_R)
        var realValues = new double[]
        {
            s.SpeedKmh,
            s.Rpms,
            Math.Max(0, s.Gear - 1),                                   // AC: 0=R,1=N,2=1st → show 0..n
            s.Throttle * 100.0,
            s.Brake    * 100.0,
            s.SteerAngle * (180.0 / Math.PI),                          // rad → degrees
            s.AccGLateral,
            s.AccGLongitudinal,
            s.Fuel,
            (s.TyreTempFL + s.TyreTempFR) * 0.5,                      // front average °C
            (s.TyreTempRL + s.TyreTempRR) * 0.5,                      // rear  average °C
            (s.TyrePressureFL + s.TyrePressureFR) * 0.5,              // front average bar
            (s.TyrePressureRL + s.TyrePressureRR) * 0.5,              // rear  average bar
        };

        for (int i = 0; i < Channels.Count && i < LapProfiles.Length && i < realValues.Length; i++)
        {
            var ch    = Channels[i];
            var ideal = LerpProfile(LapProfiles[i], LapPosition);
            ch.IdealValue = Math.Round(ideal, 2);
            ch.RealValue  = Math.Round(realValues[i], 2);
        }
    }

    /// <summary>
    /// Linearly interpolates <paramref name="profile"/> at the given lap
    /// <paramref name="pos"/> (0..1). The profile must start at pos 0 and end at pos 1.
    /// </summary>
    private static double LerpProfile((double Pos, double Ideal)[] profile, double pos)
    {
        for (int i = 0; i < profile.Length - 1; i++)
        {
            if (pos >= profile[i].Pos && pos <= profile[i + 1].Pos)
            {
                var span = profile[i + 1].Pos - profile[i].Pos;
                var lerpT = span > 0 ? (pos - profile[i].Pos) / span : 0;
                return profile[i].Ideal + lerpT * (profile[i + 1].Ideal - profile[i].Ideal);
            }
        }
        return profile[^1].Ideal;
    }

    private void UpdateLapTime()
    {
        const int baseMs = 112847; // 1:52.847
        // Range: -0.4 × 600 = -240 ms to +0.6 × 600 = +360 ms; avg bias ≈ +60 ms
        var deltaMs      = (int)((_rng.NextDouble() - 0.4) * 600);
        var realMs       = baseMs + deltaMs;
        var m            = realMs / 60000;
        var s            = (realMs % 60000) / 1000;
        var ms           = realMs % 1000;
        LapTimeReal      = $"{m}:{s:D2}.{ms:D3}";
        LapDelta         = deltaMs >= 0 ? $"+{deltaMs / 1000.0:F3}" : $"{deltaMs / 1000.0:F3}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // ── 50 ms fast-path: phase detection + corner summary ─────────────────────

    /// <summary>
    /// Fires every 50 ms on the UI thread. Keeps <see cref="CurrentPhase"/>,
    /// the <c>LastCorner*</c> observables, and <see cref="Proposals"/> up-to-date
    /// without blocking the 250 Hz AC poll loop.
    /// </summary>
    private void OnFastTick(object sender, object e)
    {
        if (!IsAcConnected) return;

        // 1. Derive current driving phase from the most recent sample
        var sample = _telemetryService.Buffer.ReadLast();
        if (sample.AcStatus == (int)AcStatus.Live)
            CurrentPhase = DerivePhaseLabel(in sample);
        else
            CurrentPhase = "—";

        // 2. Feed new samples into the corner detector (non-blocking)
        _cornerDetector.Update(_telemetryService.Buffer);

        // 3. Reflect the latest completed corner in the bindable properties
        var latest = _cornerDetector.LatestCorner;
        if (latest.HasValue && latest.Value.CornerIndex != _lastReportedCornerIndex)
        {
            var cs = latest.Value;
            _lastReportedCornerIndex  = cs.CornerIndex;
            LastCornerDirection       = cs.Direction == CornerDirection.Left  ? "Left"
                                      : cs.Direction == CornerDirection.Right ? "Right" : "—";
            LastCornerUndersteerEntry = cs.EntryFrame.UndersteerEntry;
            LastCornerUndersteerMid   = cs.MidFrame.UndersteerMid;
            LastCornerUndersteerExit  = cs.ExitFrame.UndersteerExit;
            LastCornerOversteerEntry  = cs.EntryFrame.OversteerEntry;
            LastCornerOversteerExit   = cs.ExitFrame.OversteerExit;
            LastCornerDurationText    = $"{cs.Duration.TotalSeconds:F1}s";

            // 4. Phase-aware RuleEngine evaluation
            UpdateProposalsFromCorner(in cs);
        }
    }

    /// <summary>
    /// Derives a human-readable driving phase label from a single
    /// <see cref="TelemetrySample"/>, using the same thresholds as
    /// <see cref="CornerDetector"/> for consistency.
    /// </summary>
    private static string DerivePhaseLabel(in TelemetrySample s)
    {
        if (s.Brake > 0.12f)                                                    return "Entry";
        if (s.Throttle > 0.25f)                                                 return "Exit";
        if (Math.Abs(s.AccGLateral) > 0.2f || Math.Abs(s.SteerAngle) > 0.12f) return "Mid";
        return "Straight";
    }

    /// <summary>
    /// Evaluates <see cref="RuleEngine"/> on each phase-specific
    /// <see cref="FeatureFrame"/> of the completed corner, merges results by
    /// <c>Section/Parameter</c> (keeping highest confidence), and updates
    /// <see cref="Proposals"/>. Also pushes to
    /// <see cref="SessionsViewModel.Shared"/> for the Sessions panel.
    /// </summary>
    private void UpdateProposalsFromCorner(in CornerSummary cs)
    {
        // Phase-aware evaluation: Entry for braking, Mid for balance, Exit for traction
        var entryProps = RuleEngine.Evaluate(cs.EntryFrame);
        var midProps   = RuleEngine.Evaluate(cs.MidFrame);
        var exitProps  = RuleEngine.Evaluate(cs.ExitFrame);

        // Merge by Section/Parameter, keeping the highest-confidence proposal per key
        var merged = new Dictionary<string, Proposal>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in entryProps.Concat(midProps).Concat(exitProps))
        {
            var key = $"{p.Section}/{p.Parameter}";
            if (!merged.TryGetValue(key, out var existing) || p.Confidence > existing.Confidence)
                merged[key] = p;
        }

        Proposals.Clear();
        foreach (var p in merged.Values.OrderByDescending(p => p.Confidence).Take(RuleEngine.MaxProposals))
            Proposals.Add(p);

        if (Proposals.Count > 0)
            SessionsViewModel.Shared.PushTelemetryProposals([.. Proposals]);
    }

    private static void Append(ObservableCollection<AnalysisEntry> col, string tag, string msg)
    {
        col.Add(new AnalysisEntry { Tag = tag, Message = msg });
        while (col.Count > 60) col.RemoveAt(0); // keep last 60 entries
    }

    private static void Append(ObservableCollection<AnalysisEntry> col, (string Tag, string Msg) entry)
        => Append(col, entry.Tag, entry.Msg);
}
