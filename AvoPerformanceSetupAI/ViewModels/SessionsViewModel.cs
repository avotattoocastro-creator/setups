using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    [ObservableProperty] private string _aiLevelLabel = "Nivel 1: Básico";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isConnected;

    // ── Selected items ───────────────────────────────────────────────────────
    [ObservableProperty] private string? _selectedSetupFile;
    [ObservableProperty] private SetupIteration? _selectedIteration;

    /// <summary>Controls hint text visibility: Visible when no files are loaded.</summary>
    [ObservableProperty] private Visibility _setupFilesHintVisibility = Visibility.Visible;

    // ── Backup path for Rollback ─────────────────────────────────────────────
    private string? _backupPath;
    private const string BackupExtension = ".bak";

    // ── Known simulator process names ────────────────────────────────────────
    private static readonly string[] SimProcessNames =
        ["acs", "AC2-Win64-Shipping", "AssettoCorsaCompetizione", "ACCS", "acc"];

    // ── INI sections considered tunable (AC/ACC setup structure) ────────────
    private static readonly HashSet<string> TunableSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "ALIGNMENT", "TYRES", "SUSPENSION", "FRONT", "REAR", "BRAKE", "BRAKES",
        "ELECTRONICS", "FUEL", "AERO", "DAMPERS", "GEOMETRY", "ARB", "SPRINGS"
    };

    // ── Keys that carry integer selectors, not tunable numeric values ────────
    private static readonly HashSet<string> NonTunableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "VERSION", "CARNAME", "TYPE_FRONT", "TYPE_REAR", "TYPE"
    };

    // ── Dynamic collections from file system ─────────────────────────────────
    public ObservableCollection<string> Cars { get; } = new();
    public ObservableCollection<string> Tracks { get; } = new();
    public ObservableCollection<string> SetupFiles { get; } = new();

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<SetupIteration> Iterations { get; } = new();
    public ObservableCollection<Proposal> LastProposals { get; } = new();
    public ObservableCollection<string> SetupSources { get; } = new() { "Local File", "Server", "Git Repo" };
    public ObservableCollection<string> Modes { get; } = new() { "Hotlap", "Race", "Qualify" };

    public SessionsViewModel()
    {
        // Subscribe to root-folder changes from Configuración
        SetupSettings.Instance.PropertyChanged += OnSettingsChanged;

        // If a root folder is already configured, populate cars immediately
        if (!string.IsNullOrEmpty(SetupSettings.Instance.RootFolder))
            LoadCars(SetupSettings.Instance.RootFolder);

        // Reflect the persisted AI level on startup
        UpdateAiLevelLabel(SetupSettings.Instance.AiLevel);

        AppLogger.Instance.Data($"Sesión inicializada — Modo: {Mode}");
        AppLogger.Instance.Ai($"Motor IA listo — {BrainInfo}  |  {AiLevelLabel}");
        AppLogger.Instance.Info($"Nivel de riesgo actual: {RiskLevel}");
    }

    // ── Settings change handler ───────────────────────────────────────────────

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupSettings.RootFolder))
            LoadCars(SetupSettings.Instance.RootFolder);
        else if (e.PropertyName == nameof(SetupSettings.AiLevel))
        {
            UpdateAiLevelLabel(SetupSettings.Instance.AiLevel);
            AppLogger.Instance.Ai($"Nivel de análisis de conducción actualizado — {AiLevelLabel}");
            // Regenerate proposals with the new level if a file is already selected
            if (!string.IsNullOrEmpty(SelectedSetupFile))
                LoadProposalsFromFile();
        }
    }

    // ── AI-level helpers ──────────────────────────────────────────────────────

    private static readonly string[] AiLevelNames =
    [
        "Nivel 1: Básico",
        "Nivel 2: Estándar",
        "Nivel 3: Avanzado",
        "Nivel 4: Experto",
        "Nivel 5: Profesional"
    ];

    private void UpdateAiLevelLabel(int level)
    {
        var idx = Math.Clamp(level, SetupSettings.MinAiLevel, SetupSettings.MaxAiLevel) - 1;
        AiLevelLabel = AiLevelNames[idx];
    }

    /// <summary>
    /// Returns the maximum number of proposals to generate for the given AI level.
    /// Higher levels analyse more parameters and propose a wider range of changes.
    /// </summary>
    private static int MaxProposalsForLevel(int level) => level switch
    {
        1 => 3,
        2 => 5,
        3 => 8,
        4 => 12,
        _ => 16   // level 5
    };

    /// <summary>
    /// Returns the number of entries sampled per INI section for the given AI level.
    /// </summary>
    private static int EntriesPerSectionForLevel(int level) => level switch
    {
        1 => 1,
        2 => 2,
        3 => 3,
        4 => 4,
        _ => 5   // level 5
    };

    // ── Cascading property changes ────────────────────────────────────────────

    partial void OnCarIdChanged(string value) => LoadTracks(value);
    partial void OnTrackIdChanged(string value) => LoadSetupFiles(value);

    partial void OnSelectedSetupFileChanged(string? value)
    {
        LoadProposalsFromFile();
        ApplyCommand.NotifyCanExecuteChanged();
        ApplyProposalCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedIterationChanged(SetupIteration? value)
    {
        if (value != null)
            SelectedSetupFile = value.Setup;
        ApplyCommand.NotifyCanExecuteChanged();
        ApplyProposalCommand.NotifyCanExecuteChanged();
    }

    // ── File-system loaders ───────────────────────────────────────────────────

    private void LoadCars(string rootFolder)
    {
        Cars.Clear();
        Tracks.Clear();
        SetupFiles.Clear();
        Iterations.Clear();

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
        Iterations.Clear();

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

        // Populate the Setups DataGrid with the real files found on disk
        Iterations.Clear();
        for (int i = 0; i < SetupFiles.Count; i++)
            Iterations.Add(new SetupIteration { Setup = SetupFiles[i], BestLap = "—", Iter = i, Exported = false });

        SetupFilesHintVisibility = SetupFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        AppLogger.Instance.Data($"Circuito seleccionado: {trackId}  |  Archivos de setup: {SetupFiles.Count}");
        AppLogger.Instance.Info($"Ruta de setup activa: {trackPath}");
    }

    // ── Proposal generation from real INI ────────────────────────────────────

    /// <summary>
    /// Parses the currently selected setup <c>.ini</c> file, extracts all numeric tunable
    /// parameters and populates <see cref="LastProposals"/> with small suggested adjustments.
    /// Must be called whenever <see cref="SelectedSetupFile"/> changes.
    /// </summary>
    private void LoadProposalsFromFile()
    {
        LastProposals.Clear();

        if (string.IsNullOrEmpty(SelectedSetupFile) ||
            string.IsNullOrEmpty(CarId) ||
            string.IsNullOrEmpty(TrackId))
            return;

        var filePath = Path.Combine(SetupSettings.Instance.RootFolder, CarId, TrackId, SelectedSetupFile);
        if (!File.Exists(filePath))
            return;

        try
        {
            var entries = SetupIniParser.Parse(filePath);

            // Keep only entries from known tunable sections with numeric, non-zero values
            var tunable = entries
                .Where(e =>
                    TunableSections.Contains(e.Section) &&
                    !NonTunableKeys.Contains(e.Key) &&
                    double.TryParse(e.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var v) && v != 0.0)
                .ToList();

            var aiLevel       = Math.Clamp(SetupSettings.Instance.AiLevel, SetupSettings.MinAiLevel, SetupSettings.MaxAiLevel);
            var maxProposals  = MaxProposalsForLevel(aiLevel);
            var perSection    = EntriesPerSectionForLevel(aiLevel);

            // Sample: up to `perSection` entries per section, capped at `maxProposals` proposals total
            var sample = tunable
                .GroupBy(e => e.Section)
                .SelectMany(g => g.Take(perSection))
                .Take(maxProposals)
                .ToList();

            foreach (var entry in sample)
            {
                if (!double.TryParse(entry.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var current))
                    continue;

                // Nudge: 2 % of absolute value, minimum 0.05 — always proposes a decrease
                var abs     = Math.Abs(current);
                var nudge   = abs >= 100.0 ? Math.Round(abs * 0.02, 0)
                            : abs >= 1.0   ? Math.Round(abs * 0.02, 3)
                                           : 0.05;
                var proposed = Math.Round(current - nudge, 4);
                var deltaStr = $"-{nudge.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

                LastProposals.Add(new Proposal
                {
                    Section   = entry.Section,
                    Parameter = entry.Key,
                    From      = entry.Value,
                    To        = proposed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Delta     = deltaStr
                });
            }

            AppLogger.Instance.Ai(
                $"Propuestas generadas desde '{SelectedSetupFile}' — " +
                $"{tunable.Count} parámetros disponibles, {LastProposals.Count} seleccionados  " +
                $"[{AiLevelLabel}].");

            if (tunable.Count == 0)
                AppLogger.Instance.Warn(
                    "El archivo de setup no contiene parámetros reconocibles en secciones tunables. " +
                    "Comprueba que la carpeta raíz apunta a setups de Assetto Corsa / ACC.");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al leer parámetros del setup: {ex.Message}");
        }

        ApplyProposalCommand.NotifyCanExecuteChanged();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (string.IsNullOrEmpty(CarId) || string.IsNullOrEmpty(TrackId))
        {
            AppLogger.Instance.Warn("Selecciona un coche y un circuito antes de iniciar la sesión.");
            return;
        }

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

    /// <summary>
    /// Copia el archivo de setup seleccionado a la carpeta de destino configurada en Configuración.
    /// Marca la iteración como Exported en el DataGrid.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        var outFolder = SetupSettings.Instance.OutputFolder;
        if (string.IsNullOrEmpty(outFolder))
        {
            StatusText = "● ERROR";
            AppLogger.Instance.Error("Carpeta de destino no configurada. Ve a la pestaña Configuración y selecciona la carpeta de destino.");
            return;
        }

        var sourceFile = Path.Combine(SetupSettings.Instance.RootFolder, CarId, TrackId, SelectedSetupFile!);
        var destFile = Path.Combine(outFolder, SelectedSetupFile!);

        try
        {
            Directory.CreateDirectory(outFolder);
            File.Copy(sourceFile, destFile, overwrite: true);

            // Mark the iteration as Exported in the DataGrid
            var iter = Iterations.FirstOrDefault(i => i.Setup == SelectedSetupFile);
            if (iter != null)
                iter.Exported = true;

            StatusText = "● APPLIED";
            AppLogger.Instance.Info($"Setup aplicado correctamente: {SelectedSetupFile}");
            AppLogger.Instance.Data($"Destino: {destFile}");
        }
        catch (Exception ex)
        {
            StatusText = "● APPLY ERROR";
            AppLogger.Instance.Error($"Error al copiar el setup: {ex.Message}");
        }
    }

    private bool CanApply() => !string.IsNullOrEmpty(SelectedSetupFile);

    /// <summary>
    /// Detecta si hay un proceso del simulador (AC / ACC) en ejecución y actualiza el estado de conexión.
    /// </summary>
    [RelayCommand]
    private void Connect()
    {
        var simProcessNames = SimProcessNames;
        var found = simProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);

        if (found)
        {
            IsConnected = true;
            StatusText = "● CONNECTED";
            AppLogger.Instance.Info("✔ Simulador detectado — conexión establecida.");
            AppLogger.Instance.Data($"Telemetría activa — Coche: {CarId}  Circuito: {TrackId}");
        }
        else
        {
            IsConnected = false;
            StatusText = "● SIM NOT FOUND";
            AppLogger.Instance.Warn("No se detectó ningún simulador en ejecución.");
            AppLogger.Instance.Info("Abre Assetto Corsa / ACC y pulsa Connect de nuevo.");
        }
    }

    /// <summary>
    /// Crea un backup del archivo .ini seleccionado y aplica los parámetros de LastProposals
    /// modificando directamente los valores en el archivo, respetando la sección de cada clave.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyProposal))]
    private void ApplyProposal()
    {
        var filePath = Path.Combine(SetupSettings.Instance.RootFolder, CarId, TrackId, SelectedSetupFile!);

        if (!File.Exists(filePath))
        {
            AppLogger.Instance.Error($"Archivo de setup no encontrado: {filePath}");
            return;
        }

        try
        {
            // Create backup before modifying
            _backupPath = filePath + BackupExtension;
            File.Copy(filePath, _backupPath, overwrite: true);
            AppLogger.Instance.Info($"Backup creado: {Path.GetFileName(_backupPath)}");

            // Read INI lines; apply each proposal matching by section AND key
            var lines = File.ReadAllLines(filePath).ToList();
            foreach (var proposal in LastProposals)
            {
                bool applied = false;
                var currentSection = string.Empty;

                for (int i = 0; i < lines.Count; i++)
                {
                    var trimmed = lines[i].Trim();

                    // Track current section header
                    if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                    {
                        currentSection = trimmed[1..^1].Trim();
                        continue;
                    }

                    // Match key within the correct section
                    var eqIdx = lines[i].IndexOf('=');
                    if (eqIdx > 0 &&
                        currentSection.Equals(proposal.Section, StringComparison.OrdinalIgnoreCase) &&
                        lines[i][..eqIdx].Trim().Equals(proposal.Parameter, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{proposal.Parameter}={proposal.To}";
                        AppLogger.Instance.Data(
                            $"  [{proposal.Section}] {proposal.Parameter}: {proposal.From} → {proposal.To}  (Δ {proposal.Delta})");
                        applied = true;
                        break;
                    }
                }

                if (!applied)
                    AppLogger.Instance.Warn(
                        $"  Parámetro '[{proposal.Section}] {proposal.Parameter}' no encontrado en el archivo.");
            }

            File.WriteAllLines(filePath, lines);
            StatusText = "● PROPOSAL APPLIED";
            AppLogger.Instance.Ai("Propuesta de IA aplicada al archivo de setup.");
            RollbackCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusText = "● PROPOSAL ERROR";
            AppLogger.Instance.Error($"Error al aplicar propuesta: {ex.Message}");
        }
    }

    private bool CanApplyProposal() => !string.IsNullOrEmpty(SelectedSetupFile) && LastProposals.Count > 0;

    /// <summary>
    /// Restaura el backup creado por ApplyProposal, revertiendo el archivo .ini al estado anterior.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRollback))]
    private void Rollback()
    {
        if (string.IsNullOrEmpty(_backupPath) || !File.Exists(_backupPath))
        {
            AppLogger.Instance.Warn("No hay backup disponible para restaurar.");
            return;
        }

        var originalPath = _backupPath[..^BackupExtension.Length]; // quitar ".bak"
        try
        {
            File.Copy(_backupPath, originalPath, overwrite: true);
            File.Delete(_backupPath);
            _backupPath = null;

            StatusText = "● ROLLED BACK";
            AppLogger.Instance.Warn("Rollback ejecutado — setup restaurado al estado anterior.");
            RollbackCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al restaurar backup: {ex.Message}");
        }
    }

    private bool CanRollback() => !string.IsNullOrEmpty(_backupPath) && File.Exists(_backupPath);
}
