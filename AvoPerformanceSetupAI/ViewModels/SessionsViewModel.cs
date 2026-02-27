using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.Services;

namespace AvoPerformanceSetupAI.ViewModels;

public partial class SessionsViewModel : ObservableObject
{
    // ── Config fields ────────────────────────────────────────────────────────
    [ObservableProperty] private string _carId = string.Empty;
    [ObservableProperty] private string _trackId = string.Empty;
    [ObservableProperty] private string _setupSource = "Local File";
    [ObservableProperty] private string _mode = "Hotlap";

    // ── Status ───────────────────────────────────────────────────────────────
    [ObservableProperty] private string _statusText = "● READY";
    [ObservableProperty] private string _brainInfo = "Python | latency: 12 ms";
    [ObservableProperty] private string _riskLevel = "LOW";
    [ObservableProperty] private bool _isRunning;

    // ── Selected setup file from disk ────────────────────────────────────────
    [ObservableProperty] private string? _selectedSetupFile;

    /// <summary>Controls hint text visibility: Visible when no files are loaded.</summary>
    [ObservableProperty] private Visibility _setupFilesHintVisibility = Visibility.Visible;

    // ── Dynamic collections from file system ─────────────────────────────────
    public ObservableCollection<string> Cars { get; } = new();
    public ObservableCollection<string> Tracks { get; } = new();
    public ObservableCollection<string> SetupFiles { get; } = new();

    // ── Static collections ────────────────────────────────────────────────────
    public ObservableCollection<SetupIteration> Iterations { get; } = new();
    public ObservableCollection<Proposal> LastProposals { get; } = new();
    public ObservableCollection<string> SetupSources { get; } = new() { "Local File", "Server", "Git Repo" };
    public ObservableCollection<string> Modes { get; } = new() { "Hotlap", "Race", "Qualify" };

    public SessionsViewModel()
    {
        LoadMockData();

        // Subscribe to root-folder changes from Configuración
        SetupSettings.Instance.PropertyChanged += OnSettingsChanged;

        // If a root folder is already configured, populate cars immediately
        if (!string.IsNullOrEmpty(SetupSettings.Instance.RootFolder))
            LoadCars(SetupSettings.Instance.RootFolder);

        AppLogger.Instance.Data($"Sesión inicializada — Modo: {Mode}");
        AppLogger.Instance.Ai($"Motor IA listo — {BrainInfo}");
        AppLogger.Instance.Info($"Nivel de riesgo actual: {RiskLevel}");
    }

    // ── Settings change handler ───────────────────────────────────────────────

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupSettings.RootFolder))
            LoadCars(SetupSettings.Instance.RootFolder);
    }

    // ── Cascading property changes ────────────────────────────────────────────

    partial void OnCarIdChanged(string value) => LoadTracks(value);
    partial void OnTrackIdChanged(string value) => LoadSetupFiles(value);

    // ── File-system loaders ───────────────────────────────────────────────────

    private void LoadCars(string rootFolder)
    {
        Cars.Clear();
        Tracks.Clear();
        SetupFiles.Clear();

        if (string.IsNullOrEmpty(rootFolder) || !Directory.Exists(rootFolder))
        {
            AppLogger.Instance.Warn($"Carpeta raíz no encontrada: '{rootFolder}'");
            return;
        }

        try
        {
            foreach (var dir in Directory.GetDirectories(rootFolder).OrderBy(Path.GetFileName))
                Cars.Add(Path.GetFileName(dir)!);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al leer carpetas de coches: {ex.Message}");
            return;
        }

        AppLogger.Instance.Data($"Carpeta raíz cargada: {rootFolder}");
        AppLogger.Instance.Info($"Coches encontrados: {Cars.Count}");

        // Preserve previous selection if it still exists; otherwise auto-select first
        if (!string.IsNullOrEmpty(CarId) && Cars.Contains(CarId))
            LoadTracks(CarId); // value didn't change so partial method won't fire; call explicitly
        else
            CarId = Cars.Count > 0 ? Cars[0] : string.Empty;
    }

    private void LoadTracks(string carId)
    {
        Tracks.Clear();
        SetupFiles.Clear();

        var rootFolder = SetupSettings.Instance.RootFolder;
        if (string.IsNullOrEmpty(rootFolder) || string.IsNullOrEmpty(carId))
            return;

        var carPath = Path.Combine(rootFolder, carId);
        if (!Directory.Exists(carPath))
        {
            AppLogger.Instance.Warn($"Carpeta de coche no encontrada: '{carPath}'");
            return;
        }

        try
        {
            foreach (var dir in Directory.GetDirectories(carPath).OrderBy(Path.GetFileName))
                Tracks.Add(Path.GetFileName(dir)!);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al leer circuitos: {ex.Message}");
            return;
        }

        AppLogger.Instance.Data($"Coche seleccionado: {carId}  |  Circuitos encontrados: {Tracks.Count}");

        // Preserve previous selection if it still exists; otherwise auto-select first
        if (!string.IsNullOrEmpty(TrackId) && Tracks.Contains(TrackId))
            LoadSetupFiles(TrackId);
        else
            TrackId = Tracks.Count > 0 ? Tracks[0] : string.Empty;
    }

    private void LoadSetupFiles(string trackId)
    {
        SetupFiles.Clear();

        var rootFolder = SetupSettings.Instance.RootFolder;
        if (string.IsNullOrEmpty(rootFolder) || string.IsNullOrEmpty(CarId) || string.IsNullOrEmpty(trackId))
        {
            SetupFilesHintVisibility = Visibility.Visible;
            return;
        }

        var trackPath = Path.Combine(rootFolder, CarId, trackId);
        if (!Directory.Exists(trackPath))
        {
            AppLogger.Instance.Warn($"Carpeta de circuito no encontrada: '{trackPath}'");
            SetupFilesHintVisibility = Visibility.Visible;
            return;
        }

        try
        {
            foreach (var file in Directory.GetFiles(trackPath, "*.ini").OrderBy(Path.GetFileName))
                SetupFiles.Add(Path.GetFileName(file)!);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al leer archivos de setup: {ex.Message}");
            SetupFilesHintVisibility = Visibility.Visible;
            return;
        }

        SetupFilesHintVisibility = SetupFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        AppLogger.Instance.Data($"Circuito seleccionado: {trackId}  |  Archivos de setup: {SetupFiles.Count}");
        AppLogger.Instance.Info($"Ruta de setup activa: {trackPath}");
    }

    // ── Mock data ─────────────────────────────────────────────────────────────

    private void LoadMockData()
    {
        Iterations.Add(new SetupIteration { Setup = "baseline_monza_911.ini", BestLap = "1:45.321", Iter = 0, Exported = true });
        Iterations.Add(new SetupIteration { Setup = "iter_001_hotlap.ini",     BestLap = "1:44.987", Iter = 1, Exported = true });
        Iterations.Add(new SetupIteration { Setup = "iter_002_hotlap.ini",     BestLap = "1:44.512", Iter = 2, Exported = false });
        Iterations.Add(new SetupIteration { Setup = "iter_003_hotlap.ini",     BestLap = "1:44.201", Iter = 3, Exported = false, IsSelected = true });
        Iterations.Add(new SetupIteration { Setup = "iter_004_hotlap.ini",     BestLap = "1:44.890", Iter = 4, Exported = false });

        LastProposals.Add(new Proposal { Parameter = "FrontSuspension",  From = "4.2",  To = "3.8",  Delta = "-0.4" });
        LastProposals.Add(new Proposal { Parameter = "RearAntiRollBar",  From = "6",    To = "7",    Delta = "+1"   });
        LastProposals.Add(new Proposal { Parameter = "BrakeBias",        From = "56.0", To = "55.5", Delta = "-0.5" });
        LastProposals.Add(new Proposal { Parameter = "FrontTyrePressure",From = "27.5", To = "27.2", Delta = "-0.3" });
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        IsRunning = true;
        StatusText = "● RUNNING";
        AppLogger.Instance.Info($"Sesión INICIADA — Coche: {CarId}  Circuito: {TrackId}  Modo: {Mode}");
        AppLogger.Instance.Ai("Motor IA activado. Esperando datos de telemetría...");
        AppLogger.Instance.Data("Canal de datos en tiempo real: ABIERTO");
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        IsRunning = false;
        StatusText = "● READY";
        AppLogger.Instance.Info("Sesión DETENIDA por el usuario.");
        AppLogger.Instance.Ai("Motor IA pausado.");
        AppLogger.Instance.Data("Canal de datos en tiempo real: CERRADO");
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private bool CanStop() => IsRunning;

    [RelayCommand]
    private void Apply()
    {
        StatusText = "● APPLYING...";
        AppLogger.Instance.Ai("Calculando nueva iteración de setup...");
        AppLogger.Instance.Data($"Aplicando iteración #{Iterations.Count} al simulador.");
    }

    [RelayCommand]
    private void ApplyProposal()
    {
        StatusText = "● PROPOSAL APPLIED";
        AppLogger.Instance.Ai("Propuesta de IA aceptada y aplicada al setup activo.");
        foreach (var p in LastProposals)
            AppLogger.Instance.Data($"  Parámetro: {p.Parameter}  {p.From} → {p.To}  (Δ {p.Delta})");
    }

    [RelayCommand]
    private void Rollback()
    {
        StatusText = "● ROLLED BACK";
        AppLogger.Instance.Warn("Rollback ejecutado — setup restaurado a la iteración anterior.");
    }

    [RelayCommand]
    private void Connect()
    {
        StatusText = "● CONNECTED";
        AppLogger.Instance.Info("Conexión con el simulador establecida.");
        AppLogger.Instance.Data($"Telemetría activa — Coche: {CarId}  Circuito: {TrackId}");
    }
}
