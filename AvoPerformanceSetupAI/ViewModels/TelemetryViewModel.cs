using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.Services;

namespace AvoPerformanceSetupAI.ViewModels;

public partial class TelemetryViewModel : ObservableObject
{
    private DispatcherQueue? _dispatcher;
    private System.Threading.Timer? _updateTimer;
    private int  _tick;
    private readonly Random _rng = new(Environment.TickCount);

    // ── Observable state ─────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isSimulating;
    [ObservableProperty] private string _statusText       = "● DETENIDO";
    [ObservableProperty] private string _lapTimeReal      = "--:--.---";
    [ObservableProperty] private string _lapTimeIdeal     = "1:52.847";
    [ObservableProperty] private string _lapDelta         = "---";
    [ObservableProperty] private double _lapPosition;
    [ObservableProperty] private string _lapPositionText  = "Pos:  0%";

    // ── Collections ──────────────────────────────────────────────────────────

    /// <summary>Telemetry channels — each holds both the real (live) and ideal (target) value.</summary>
    public ObservableCollection<TelemetryChannel> Channels    { get; } = new();

    /// <summary>Car-behaviour analysis log.</summary>
    public ObservableCollection<AnalysisEntry>    BehaviorLogs { get; } = new();

    /// <summary>Driving-time analysis log.</summary>
    public ObservableCollection<AnalysisEntry>    DrivingLogs  { get; } = new();

    /// <summary>Setup-improvement steps log.</summary>
    public ObservableCollection<AnalysisEntry>    SetupLogs    { get; } = new();

    // ── Channel definitions ───────────────────────────────────────────────────

    private static readonly (string Name, string Unit, double Ideal)[] ChannelDefs =
    [
        ("SPEED",    "km/h", 245.0),
        ("RPM",      "rpm",  6800.0),
        ("GEAR",     "",       5.0),
        ("THROTTLE", "%",     93.0),
        ("BRAKE",    "%",      7.0),
        ("STEER",    "°",      2.1),
        ("LAT_G",    "G",      1.42),
        ("LONG_G",   "G",     -0.32),
        ("FUEL",     "L",     43.5),
        ("T_F",      "°C",   83.0),
        ("T_R",      "°C",   88.0),
        ("P_F",      "bar",   1.80),
        ("P_R",      "bar",   1.72),
    ];

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
    }

    /// <summary>
    /// Must be called once from the UI thread (page constructor) to allow the timer
    /// callbacks to marshal back onto the UI dispatcher.
    /// </summary>
    public void Initialize(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartSimulation()
    {
        IsSimulating = true;
        StatusText   = "● ACTIVO";
        _tick        = 0;
        _updateTimer = new System.Threading.Timer(OnTick, null, 0, 800);
        AppLogger.Instance.Ai("Telemetría en tiempo real ACTIVADA — tick cada 800 ms.");
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
        AppLogger.Instance.Info("Paneles de análisis de telemetría limpiados.");
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
        });
    }

    private void UpdateChannels()
    {
        foreach (var ch in Channels)
        {
            // Oscillate real value ±4 % around ideal (factor in range [0.96, 1.04])
            var noise = (_rng.NextDouble() - 0.5) * 0.08;
            ch.RealValue = Math.Round(ch.IdealValue * (1.0 + noise), 2);
        }
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

    private static void Append(ObservableCollection<AnalysisEntry> col, string tag, string msg)
    {
        col.Add(new AnalysisEntry { Tag = tag, Message = msg });
        while (col.Count > 60) col.RemoveAt(0); // keep last 60 entries
    }

    private static void Append(ObservableCollection<AnalysisEntry> col, (string Tag, string Msg) entry)
        => Append(col, entry.Tag, entry.Msg);
}
