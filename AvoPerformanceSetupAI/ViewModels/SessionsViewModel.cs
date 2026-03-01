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
using AvoPerformanceSetupAI.Services.Agent;
using AvoPerformanceSetupAI.Services.Setup;

namespace AvoPerformanceSetupAI.ViewModels;

public partial class SessionsViewModel : ObservableObject
{
    /// <summary>Application-wide shared instance used by all pages.</summary>
    public static SessionsViewModel Shared { get; } = new SessionsViewModel();

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
    [ObservableProperty] private bool _isConnected;

    /// <summary>True when the app is configured for Remote Agent mode.</summary>
    public bool IsRemoteMode => SetupSettings.Instance.Mode == AppMode.Remote;

    /// <summary>
    /// Short connection status badge text for the Telemetry page header.
    /// "REMOTE CONNECTED" / empty.
    /// </summary>
    [ObservableProperty] private string _agentStatusText = string.Empty;

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

    /// <summary>
    /// Diagnostic log entries produced by Apply Proposal and similar commands.
    /// Forwarded to the Agent Logs tab by <see cref="TelemetryViewModel"/>.
    /// </summary>
    public ObservableCollection<AgentLogEntry> Logs { get; } = new();

    private void AddLog(string msg, string lvl = "SYS") =>
        Logs.Add(new AgentLogEntry
        {
            TUtc = DateTime.UtcNow.ToString("O"),
            Lvl  = lvl,
            Cat  = "Client",
            Msg  = msg,
        });

    /// <summary>
    /// All numeric tunable parameters from the currently loaded setup file,
    /// classified by <c>SetupParameterClassifier</c>.
    /// Consumed by the Setup Diff feature.
    /// </summary>
    public ObservableCollection<SetupParameter> ParsedParameters { get; } = new();
    public ObservableCollection<string> SetupSources { get; } = new() { "Local File", "Server", "Git Repo" };
    public ObservableCollection<string> Modes { get; } = new() { "Hotlap", "Race", "Qualify" };

    public SessionsViewModel()
    {
        // Subscribe to root-folder and mode changes from Configuración
        SetupSettings.Instance.PropertyChanged += OnSettingsChanged;

        // If a root folder is already configured, populate cars immediately
        if (!string.IsNullOrEmpty(SetupSettings.Instance.RootFolder))
            _ = LoadCarsAsync(SetupSettings.Instance.RootFolder);

        AppLogger.Instance.Data($"Sesión inicializada — Modo: {Mode}");
        AppLogger.Instance.Ai($"Motor IA listo — {BrainInfo}");
        AppLogger.Instance.Info($"Nivel de riesgo actual: {RiskLevel}");
    }

    // ── Provider factory ──────────────────────────────────────────────────────

    /// <summary>
    /// Cached remote client; recreated whenever the remote connection settings change.
    /// Disposed together with the provider when a new one is created.
    /// </summary>
    private AgentApiClient? _cachedAgentClient;
    private (string host, int port, string token) _cachedClientKey;

    private AgentApiClient GetOrCreateAgentClient()
    {
        var s = SetupSettings.Instance;
        var key = (s.RemoteHost, s.RemotePort, s.RemoteToken);
        if (_cachedAgentClient is null || _cachedClientKey != key)
        {
            _cachedAgentClient?.Dispose();
            _cachedAgentClient = new AgentApiClient(s.RemoteHost, s.RemotePort, s.RemoteToken);
            _cachedClientKey   = key;
        }
        return _cachedAgentClient;
    }

    private ISetupLibraryProvider CreateProvider()
    {
        if (SetupSettings.Instance.Mode == AppMode.Remote)
            return new RemoteSetupLibraryProvider(GetOrCreateAgentClient());

        return new LocalSetupLibraryProvider((Application.Current as App)!.MainWindow!);
    }

    /// <summary>
    /// Public accessor so adjacent ViewModels (e.g. SetupDiffViewModel) can load
    /// setup files using the same provider strategy (Local / Remote) as the sessions page.
    /// </summary>
    internal ISetupLibraryProvider CreateProviderPublic() => CreateProvider();

    private ISetupSaver CreateSaver()
    {
        if (SetupSettings.Instance.Mode == AppMode.Remote)
            return new RemoteSetupSaver(GetOrCreateAgentClient());

        return new LocalSetupSaver();
    }

    // ── Settings change handler ───────────────────────────────────────────────

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SetupSettings.RootFolder):
                _ = LoadCarsAsync(SetupSettings.Instance.RootFolder);
                break;
            case nameof(SetupSettings.Mode):
                OnPropertyChanged(nameof(IsRemoteMode));
                _ = LoadCarsAsync(SetupSettings.Instance.RootFolder);
                break;
            case nameof(SetupSettings.RemoteHost):
            case nameof(SetupSettings.RemotePort):
            case nameof(SetupSettings.RemoteToken):
                // Re-load if already in Remote mode
                if (IsRemoteMode)
                    _ = LoadCarsAsync(SetupSettings.Instance.RootFolder);
                break;
        }
    }

    // ── Cascading property changes ────────────────────────────────────────────

    partial void OnCarIdChanged(string value)   => _ = LoadTracksAsync(value);
    partial void OnTrackIdChanged(string value) => _ = LoadSetupFilesAsync(value);

    partial void OnSelectedSetupFileChanged(string? value)
    {
        _ = LoadProposalsFromFileAsync();
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

    // ── Provider-based loaders ────────────────────────────────────────────────

    /// <summary>
    /// Called from the Sesiones page "Select Folder" button (via command) so the
    /// provider can show its own picker (local or remote).
    /// </summary>
    [RelayCommand]
    private async Task SelectRootFolderAsync()
    {
        var provider = CreateProvider();
        var path = await provider.SelectRootAsync();
        if (path is not null)
        {
            AppLogger.Instance.Info($"Carpeta raíz configurada: {path}");
            await LoadCarsAsync(path);
        }
    }

    private async Task LoadCarsAsync(string rootFolder)
    {
        Cars.Clear();
        Tracks.Clear();
        SetupFiles.Clear();
        Iterations.Clear();

        var provider = CreateProvider();
        try
        {
            var cars = await provider.GetCarsAsync();
            foreach (var c in cars) Cars.Add(c);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al leer carpetas de coches: {ex.Message}");
            return;
        }

        AppLogger.Instance.Data($"Carpeta raíz cargada: {rootFolder}");
        AppLogger.Instance.Info($"Coches encontrados: {Cars.Count}");

        if (!string.IsNullOrEmpty(CarId) && Cars.Contains(CarId))
            await LoadTracksAsync(CarId);
        else
            CarId = Cars.Count > 0 ? Cars[0] : string.Empty;
    }

    private async Task LoadTracksAsync(string carId)
    {
        Tracks.Clear();
        SetupFiles.Clear();
        Iterations.Clear();

        if (string.IsNullOrEmpty(carId)) return;

        var provider = CreateProvider();
        try
        {
            var tracks = await provider.GetTracksAsync(carId);
            foreach (var t in tracks) Tracks.Add(t);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al leer circuitos: {ex.Message}");
            return;
        }

        AppLogger.Instance.Data($"Coche seleccionado: {carId}  |  Circuitos encontrados: {Tracks.Count}");

        if (!string.IsNullOrEmpty(TrackId) && Tracks.Contains(TrackId))
            await LoadSetupFilesAsync(TrackId);
        else
            TrackId = Tracks.Count > 0 ? Tracks[0] : string.Empty;
    }

    private async Task LoadSetupFilesAsync(string trackId)
    {
        SetupFiles.Clear();
        Iterations.Clear();

        if (string.IsNullOrEmpty(CarId) || string.IsNullOrEmpty(trackId))
        {
            SetupFilesHintVisibility = Visibility.Visible;
            return;
        }

        var provider = CreateProvider();
        try
        {
            var items = await provider.GetSetupsAsync(CarId, trackId);
            foreach (var s in items) SetupFiles.Add(s.FileName);

            Iterations.Clear();
            for (int i = 0; i < SetupFiles.Count; i++)
                Iterations.Add(new SetupIteration { Setup = SetupFiles[i], BestLap = "—", Iter = i, Exported = false });
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al leer archivos de setup: {ex.Message}");
            SetupFilesHintVisibility = Visibility.Visible;
            return;
        }

        SetupFilesHintVisibility = SetupFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AppLogger.Instance.Data($"Circuito seleccionado: {trackId}  |  Archivos de setup: {SetupFiles.Count}");
    }

    // ── Proposal generation from real INI ────────────────────────────────────

    /// <summary>
    /// Parses the currently selected setup <c>.ini</c> file (local or remote),
    /// extracts all numeric tunable parameters and populates <see cref="LastProposals"/>.
    /// </summary>
    private async Task LoadProposalsFromFileAsync()
    {
        LastProposals.Clear();

        if (string.IsNullOrEmpty(SelectedSetupFile) ||
            string.IsNullOrEmpty(CarId) ||
            string.IsNullOrEmpty(TrackId))
            return;

        string iniText;
        try
        {
            iniText = await CreateProvider().ReadSetupTextAsync(CarId, TrackId, SelectedSetupFile);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al leer setup: {ex.Message}");
            return;
        }

        try
        {
            BuildProposals(SetupIniParser.ParseText(iniText));
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al leer parámetros del setup: {ex.Message}");
        }

        ApplyProposalCommand.NotifyCanExecuteChanged();
    }

    private void BuildProposals(IEnumerable<IniEntry> entries)
    {
        var allEntries = entries as List<IniEntry> ?? entries.ToList();

        var tunable = allEntries
            .Where(e =>
                !NonTunableKeys.Contains(e.Key) &&
                double.TryParse(e.Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v) && v != 0.0)
            .ToList();

        var sectionCount = allEntries.Select(e => e.Section).Distinct().Count();
        AppLogger.Instance.Data($"INI parsed: sections={sectionCount}, numericParams={tunable.Count}");

        if (tunable.Count == 0)
        {
            foreach (var e in allEntries.Take(20))
                AppLogger.Instance.Data($"  candidate: [{e.Section}] {e.Key}={e.Value}");
        }

        // ── Build and classify SetupParameter list ────────────────────────────
        ParsedParameters.Clear();
        foreach (var e in tunable)
        {
            var sp = SetupParameter.FromIniEntry(e);
            if (sp is null) continue;
            SetupParameterClassifier.Classify(sp);
            ParsedParameters.Add(sp);
        }

        var sample = tunable
            .GroupBy(e => e.Section)
            .SelectMany(g => g.Take(2))
            .Take(6)
            .ToList();

        foreach (var entry in sample)
        {
            if (!double.TryParse(entry.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var current))
                continue;

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
            $"{tunable.Count} parámetros disponibles, {LastProposals.Count} seleccionados.");

        if (tunable.Count == 0)
            AppLogger.Instance.Warn(
                "El archivo de setup no contiene parámetros numéricos reconocibles.");
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

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var saver             = CreateSaver();
        var iniText           = string.Empty;
        var versionedFileName = NextVersionedFileName(SelectedSetupFile!);

        // Read the current setup text (needed for remote save; for local we still copy the file)
        try
        {
            iniText = await CreateProvider().ReadSetupTextAsync(CarId, TrackId, SelectedSetupFile!);
        }
        catch (Exception ex)
        {
            StatusText = "● ERROR";
            AppLogger.Instance.Error($"Error al leer setup para guardar: {ex.Message}");
            return;
        }

        try
        {
            var savedPath = await saver.SaveAsync(CarId, TrackId, versionedFileName, iniText);

            StatusText = "● APPLIED";
            AppLogger.Instance.Info($"Setup guardado como: {versionedFileName}");
            AppLogger.Instance.Data(IsRemoteMode
                ? $"Setup guardado en PC simulador: {savedPath}"
                : $"Destino: {savedPath}");

            // Refresh file list so the new versioned file appears, then select it.
            await LoadSetupFilesAsync(TrackId);
            SelectedSetupFile = versionedFileName;
        }
        catch (Exception ex)
        {
            StatusText = "● APPLY ERROR";
            AppLogger.Instance.Error($"Error al guardar el setup: {ex.Message}");
        }
    }

    private bool CanApply() => !string.IsNullOrEmpty(SelectedSetupFile);

    /// <summary>
    /// Returns a new versioned file name based on <paramref name="baseName"/>.
    /// <para>
    /// Pattern: <c>{stem}__AI__v{NNN}{ext}</c>, where NNN is the next three-digit
    /// integer after the highest existing version found in <see cref="SetupFiles"/>.
    /// </para>
    /// <example>
    /// If <c>SetupFiles</c> contains "Supra MKIV Race mid__AI__v001.ini" and
    /// "Supra MKIV Race mid__AI__v002.ini", the next name returned is
    /// "Supra MKIV Race mid__AI__v003.ini".
    /// </example>
    /// </summary>
    private string NextVersionedFileName(string baseName)
    {
        var ext    = Path.GetExtension(baseName);
        var stem   = Path.GetFileNameWithoutExtension(baseName);
        var prefix = $"{stem}__AI__v";

        int maxVersion = 0;
        foreach (var f in SetupFiles)
        {
            var fStem = Path.GetFileNameWithoutExtension(f);
            if (fStem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Only accept pure-digit suffixes (e.g. "001", "002") to avoid false matches.
                var numStr = fStem[prefix.Length..];
                if (numStr.Length > 0 &&
                    numStr.All(char.IsDigit) &&
                    int.TryParse(numStr, out int n) &&
                    n > maxVersion)
                    maxVersion = n;
            }
        }

        return $"{prefix}{(maxVersion + 1):D3}{ext}";
    }


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

    [RelayCommand(CanExecute = nameof(CanApplyProposal))]
    private async Task ApplyProposalAsync()
    {
        System.Diagnostics.Debug.WriteLine("[SessionsVM] APPLY CLICKED");
        AddLog("APPLY CLICKED");

        // Read current INI via provider (works local or remote)
        string iniText;
        try
        {
            iniText = await CreateProvider().ReadSetupTextAsync(CarId, TrackId, SelectedSetupFile!);
        }
        catch (Exception ex)
        {
            var readErr = $"APPLY READ ERROR: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[SessionsVM] {readErr}");
            AddLog(readErr, "ERR");
            AppLogger.Instance.Error($"Error al leer setup: {ex.Message}");
            return;
        }

        // Apply proposals in-memory
        var lines = SetupIniParser.NormalizeText(iniText)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .ToList();
        foreach (var proposal in LastProposals)
        {
            bool applied = false;
            var currentSection = string.Empty;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    currentSection = trimmed[1..^1].Trim();
                    continue;
                }

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

        var modifiedText = string.Join("\n", lines);
        var versionedName = NextVersionedFileName(SelectedSetupFile!);

        // Save versioned file (local: writes to circuit folder; remote: sends to Agent)
        try
        {
            string savedPath;
            if (IsRemoteMode)
            {
                var postUrl = $"{SetupSettings.Instance.AgentBaseUrl}/api/reference/setups/save";
                System.Diagnostics.Debug.WriteLine($"[SessionsVM] APPLY REMOTE: sending POST {postUrl}");
                AddLog($"APPLY REMOTE: sending POST {postUrl}");

                savedPath = await CreateSaver().SaveAsync(CarId, TrackId, versionedName, modifiedText);

                System.Diagnostics.Debug.WriteLine($"[SessionsVM] APPLY REMOTE: OK saved as {versionedName}");
                AddLog($"APPLY REMOTE: OK saved as {versionedName}");
                AppLogger.Instance.Ai($"Propuesta de IA aplicada y guardada en PC simulador: {savedPath}");
            }
            else
            {
                // Local: write the new versioned file (original is untouched — no overwrite/backup needed)
                var destFolder = Path.Combine(SetupSettings.Instance.RootFolder, CarId, TrackId);
                Directory.CreateDirectory(destFolder);
                var filePath = Path.Combine(destFolder, versionedName);
                await File.WriteAllTextAsync(filePath, modifiedText);
                savedPath = filePath;
                AppLogger.Instance.Ai($"Propuesta de IA aplicada al archivo de setup.");
            }

            StatusText = "● PROPOSAL APPLIED";
            AppLogger.Instance.Info($"Setup guardado como: {versionedName}  →  {savedPath}");

            // Refresh file list so the new versioned file appears, then select it.
            await LoadSetupFilesAsync(TrackId);
            SelectedSetupFile = versionedName;

            // Reload proposals from the newly selected versioned file so ParsedParameters
            // (Setup Diff) reflects the applied changes.
            await LoadProposalsFromFileAsync();
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException is not null ? $" ({ex.InnerException.Message})" : string.Empty;
            var failMsg = $"APPLY REMOTE: ERROR [{ex.GetType().Name}] {ex.Message}{inner}";
            System.Diagnostics.Debug.WriteLine($"[SessionsVM] {failMsg}");
            AddLog(failMsg, "ERR");
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

    // ── Telemetry integration ─────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="TelemetryViewModel"/> on each setup-adjustment tick.
    /// Adds a new proposal or replaces an existing one with the same Section+Parameter
    /// in <see cref="LastProposals"/>, and increments the selected iteration counter.
    /// Does nothing when no iteration is selected.
    /// </summary>
    public void PushTelemetryProposal(Proposal p)
    {
        if (SelectedIteration is null) return;

        bool replaced = false;
        for (int i = 0; i < LastProposals.Count; i++)
        {
            if (LastProposals[i].Section.Equals(p.Section, StringComparison.OrdinalIgnoreCase) &&
                LastProposals[i].Parameter.Equals(p.Parameter, StringComparison.OrdinalIgnoreCase))
            {
                LastProposals[i] = p;
                replaced = true;
                break;
            }
        }

        if (!replaced)
            LastProposals.Add(p);

        SelectedIteration.Iter++;
    }

    /// <summary>
    /// Batch variant of <see cref="PushTelemetryProposal"/>: applies all proposals
    /// from <paramref name="proposals"/> in a single pass, updating the iteration
    /// counter only once. Does nothing when no iteration is selected or the array
    /// is empty.
    /// </summary>
    public void PushTelemetryProposals(Proposal[] proposals)
    {
        if (SelectedIteration is null || proposals.Length == 0) return;

        foreach (var p in proposals)
        {
            bool replaced = false;
            for (int i = 0; i < LastProposals.Count; i++)
            {
                if (LastProposals[i].Section.Equals(p.Section, StringComparison.OrdinalIgnoreCase) &&
                    LastProposals[i].Parameter.Equals(p.Parameter, StringComparison.OrdinalIgnoreCase))
                {
                    LastProposals[i] = p;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
                LastProposals.Add(p);
        }

        SelectedIteration.Iter++;
    }
}
