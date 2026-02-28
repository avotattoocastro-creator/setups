using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.Services;

namespace AvoPerformanceSetupAI.ViewModels;

/// <summary>
/// ViewModel for the real-time Assetto Corsa telemetry tab.
/// <para>
/// A <see cref="DispatcherQueueTimer"/> polls AC's shared memory at 10 Hz (every 100 ms)
/// and updates all bindable gauge properties on the UI thread.  A full analysis pass —
/// which runs <see cref="TelemetryAnalyzer"/> and the MLP neural network to generate
/// setup proposals — is performed every 3 seconds (every 30 ticks).
/// </para>
/// </summary>
public partial class TelemetryViewModel : ObservableObject, IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private static readonly string WeightsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AvoPerformanceSetupAI",
        "ml_weights.bin");

    /// <summary>Run analysis every this many 100 ms ticks (= 3 seconds).</summary>
    private const int AnalysisInterval = 30;

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly MlSetupOptimizer   _optimizer = new();
    private readonly TelemetryAnalyzer  _analyzer;
    private readonly DispatcherQueueTimer _timer;
    private int _analysisTick;

    // ── Observable properties — connection ────────────────────────────────────

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionDot = "🔴";

    [ObservableProperty]
    private string _carModel = "—";

    [ObservableProperty]
    private string _trackName = "—";

    // ── Observable properties — live car data ─────────────────────────────────

    [ObservableProperty]
    private string _speedText = "— km/h";

    [ObservableProperty]
    private string _gearText = "—";

    [ObservableProperty]
    private string _rpmText = "—";

    [ObservableProperty]
    private double _throttlePct;

    [ObservableProperty]
    private double _brakePct;

    [ObservableProperty]
    private string _surfaceGripText = "—";

    // ── Observable properties — tyre temperatures (FL/FR/RL/RR) ──────────────

    [ObservableProperty] private string _tempFL = "—°C";
    [ObservableProperty] private string _tempFR = "—°C";
    [ObservableProperty] private string _tempRL = "—°C";
    [ObservableProperty] private string _tempRR = "—°C";

    /// <summary>Colour-coded temperature status icons (🔵 cold, 🟢 optimal, 🔴 overheating).</summary>
    [ObservableProperty] private string _tempIconFL = "⚫";
    [ObservableProperty] private string _tempIconFR = "⚫";
    [ObservableProperty] private string _tempIconRL = "⚫";
    [ObservableProperty] private string _tempIconRR = "⚫";

    // ── Observable properties — wheel slip (FL/FR/RL/RR) ─────────────────────

    [ObservableProperty] private string _slipFL = "—";
    [ObservableProperty] private string _slipFR = "—";
    [ObservableProperty] private string _slipRL = "—";
    [ObservableProperty] private string _slipRR = "—";

    // ── Observable properties — analysis status ───────────────────────────────

    [ObservableProperty]
    private string _statusText = "● DESCONECTADO — Inicia Assetto Corsa para activar el análisis";

    [ObservableProperty]
    private string _lastAnalysisText = "Esperando datos de telemetría...";

    /// <summary>Setup proposals generated from the latest telemetry analysis.</summary>
    public ObservableCollection<Proposal> AiProposals { get; } = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public TelemetryViewModel()
    {
        _analyzer = new TelemetryAnalyzer(_optimizer);

        // Restore previously trained neural-network weights (if available)
        try
        {
            if (File.Exists(WeightsPath))
            {
                _optimizer.LoadWeights(WeightsPath);
                AppLogger.Instance.Ai("Telemetría: pesos ML restaurados desde sesión anterior.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"Telemetría: no se pudieron cargar los pesos ML: {ex.Message}");
        }

        // Create a UI-thread timer using the current dispatcher queue.
        // The ViewModel is always constructed on the UI thread (from Page ctor),
        // so GetForCurrentThread() is guaranteed to return a non-null queue.
        var dq = DispatcherQueue.GetForCurrentThread()
                 ?? throw new InvalidOperationException(
                        "TelemetryViewModel must be created on the UI thread.");

        _timer = dq.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick    += OnTick;
        _timer.Start();
    }

    // ── Timer tick — runs on the UI thread at 10 Hz ───────────────────────────

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var snapshot = AcTelemetryReader.TryRead();

        bool connected = snapshot?.IsLive ?? false;

        if (connected != IsConnected)
        {
            IsConnected   = connected;
            ConnectionDot = connected ? "🟢" : "🔴";
        }

        if (snapshot is null || !snapshot.IsLive)
        {
            StatusText = "● DESCONECTADO — Inicia Assetto Corsa para activar el análisis";
            return;
        }

        // ── Update live gauges ────────────────────────────────────────────────
        UpdateLiveData(snapshot);

        // ── Feed analyzer rolling window ──────────────────────────────────────
        _analyzer.AddSample(snapshot);

        // ── Run analysis every AnalysisInterval ticks (≈ 3 s) ────────────────
        if (++_analysisTick >= AnalysisInterval)
        {
            _analysisTick = 0;
            RunAnalysis();
        }
    }

    // ── Live data update ──────────────────────────────────────────────────────

    private void UpdateLiveData(AcTelemetrySnapshot s)
    {
        if (!string.IsNullOrEmpty(s.CarModel))  CarModel  = s.CarModel;
        if (!string.IsNullOrEmpty(s.TrackName)) TrackName = s.TrackName;

        SpeedText = $"{s.SpeedKmh:F0} km/h";
        GearText  = s.Gear switch { 0 => "R", 1 => "N", var g => (g - 1).ToString() };
        RpmText   = s.Rpms.ToString("N0");

        ThrottlePct = Math.Clamp(s.Throttle, 0.0, 1.0);
        BrakePct    = Math.Clamp(s.Brake,    0.0, 1.0);

        SurfaceGripText = $"{s.SurfaceGrip:P0}";

        // Tyre temperatures
        TempFL = FormatTemp(s.TyreCoreTemp[0]);
        TempFR = FormatTemp(s.TyreCoreTemp[1]);
        TempRL = FormatTemp(s.TyreCoreTemp[2]);
        TempRR = FormatTemp(s.TyreCoreTemp[3]);

        TempIconFL = TempIcon(s.TyreCoreTemp[0]);
        TempIconFR = TempIcon(s.TyreCoreTemp[1]);
        TempIconRL = TempIcon(s.TyreCoreTemp[2]);
        TempIconRR = TempIcon(s.TyreCoreTemp[3]);

        // Wheel slip
        SlipFL = FormatSlip(s.WheelSlip[0]);
        SlipFR = FormatSlip(s.WheelSlip[1]);
        SlipRL = FormatSlip(s.WheelSlip[2]);
        SlipRR = FormatSlip(s.WheelSlip[3]);

        StatusText = $"● CONECTADO  |  {s.CarModel}  |  {s.TrackName}" +
                     $"  |  Vuelta {s.CompletedLaps}  |  {SessionTypeText(s.SessionType)}";
    }

    // ── Analysis ──────────────────────────────────────────────────────────────

    private void RunAnalysis()
    {
        var setupPath  = SetupSettings.Instance.CurrentSetupPath;
        var iniEntries = new List<IniEntry>();

        if (!string.IsNullOrEmpty(setupPath) && File.Exists(setupPath))
        {
            try   { iniEntries = SetupIniParser.Parse(setupPath); }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"Telemetría: error leyendo setup: {ex.Message}");
            }
        }

        var proposals = _analyzer.Analyse(iniEntries);

        if (proposals.Count > 0)
        {
            AiProposals.Clear();
            foreach (var p in proposals)
                AiProposals.Add(p);

            LastAnalysisText =
                $"Último análisis: {DateTime.Now:HH:mm:ss}  —  {proposals.Count} propuesta(s) generada(s)";
            AppLogger.Instance.Ai(
                $"Telemetría: {proposals.Count} propuesta(s) generada(s) automáticamente.");
        }
        else
        {
            LastAnalysisText =
                $"Último análisis: {DateTime.Now:HH:mm:ss}  —  sin problemas detectados";
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Forces an immediate analysis on the next timer tick.</summary>
    [RelayCommand]
    private void AnalyzeNow()
    {
        _analysisTick = AnalysisInterval;
        AppLogger.Instance.Ai("Telemetría: análisis manual solicitado.");
    }

    /// <summary>Positive feedback: trains the neural network that the current proposals were useful.</summary>
    [RelayCommand]
    private void AcceptProposals()
    {
        TrainOnProposals(label: 1.0);
        AppLogger.Instance.Ai("Telemetría: feedback positivo aplicado al optimizador ML.");
    }

    /// <summary>Negative feedback: trains the neural network that the current proposals were not useful.</summary>
    [RelayCommand]
    private void RejectProposals()
    {
        TrainOnProposals(label: 0.0);
        AppLogger.Instance.Ai("Telemetría: feedback negativo aplicado al optimizador ML.");
    }

    // ── Training ──────────────────────────────────────────────────────────────

    private void TrainOnProposals(double label)
    {
        var setupPath = SetupSettings.Instance.CurrentSetupPath;
        if (string.IsNullOrEmpty(setupPath) || !File.Exists(setupPath)) return;

        try
        {
            var iniEntries = SetupIniParser.Parse(setupPath);
            foreach (var proposal in AiProposals)
            {
                if (proposal.From == "—") continue;

                var entry = iniEntries.FirstOrDefault(e =>
                    e.Section.Equals(proposal.Section, StringComparison.OrdinalIgnoreCase) &&
                    e.Key.Equals(proposal.Parameter,   StringComparison.OrdinalIgnoreCase));
                if (entry is null) continue;

                double deltaFactor = 0;
                if (double.TryParse(proposal.Delta.TrimStart('+'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double nudge) &&
                    double.TryParse(proposal.From,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double fromVal) &&
                    Math.Abs(fromVal) > 1e-10)
                {
                    deltaFactor = nudge / fromVal;
                }

                _optimizer.Train(NlpService.BuildFeatures(entry, deltaFactor), label);
            }

            // Persist updated weights
            var dir = Path.GetDirectoryName(WeightsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            _optimizer.SaveWeights(WeightsPath);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Telemetría: error en entrenamiento ML: {ex.Message}");
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Stops the polling timer. Call when the page is unloaded.</summary>
    public void StopMonitoring() => _timer.Stop();

    public void Dispose() => _timer.Stop();

    // ── Static helpers ────────────────────────────────────────────────────────

    private static string FormatTemp(float t) =>
        t > 0 ? $"{t:F1}°C" : "—°C";

    private static string FormatSlip(float s) =>
        $"{Math.Max(0f, s):F1}";

    /// <summary>
    /// Returns a coloured circle emoji representing the tyre temperature status.
    /// 🔵 = cold, 🟡 = warming, 🟢 = optimal (80–105 °C), 🟠 = hot, 🔴 = overheating.
    /// </summary>
    private static string TempIcon(float temp) => temp switch
    {
        <= 0f   => "⚫",
        < 60f   => "🔵",
        < 80f   => "🟡",
        <= 105f => "🟢",
        <= 120f => "🟠",
        _       => "🔴",
    };

    private static string SessionTypeText(int type) => type switch
    {
        0 => "Práctica libre",
        1 => "Clasificación",
        2 => "Carrera",
        3 => "Vuelta rápida",
        _ => "Sesión",
    };
}
