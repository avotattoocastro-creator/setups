using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Text.Json;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// Application-wide settings singleton.
/// Holds the root folder path for setup files so all pages can observe it.
/// Settings are persisted to a JSON file in LocalApplicationData so they
/// survive application restarts regardless of MSIX packaging context.
/// </summary>
public sealed partial class SetupSettings : ObservableObject
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AvoPerformanceSetupAI",
        "settings.json");

    public static SetupSettings Instance { get; } = new SetupSettings();

    [ObservableProperty]
    private string _rootFolder = string.Empty;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    // Prevents Save() from running while the constructor is loading stored values.
    private bool _isInitializing = true;
    private readonly object _saveLock = new();

    private SetupSettings()
    {
        Load();
        _isInitializing = false;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data is not null)
                {
                    RootFolder = data.RootFolder ?? string.Empty;
                    OutputFolder = data.OutputFolder ?? string.Empty;
                }
            }
        }
        catch (Exception ex) { AppLogger.Instance.Warn($"[Settings] Error al cargar configuración: {ex.Message}"); }
    }

    private void Save()
    {
        if (_isInitializing) return;
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
                var data = new SettingsData { RootFolder = RootFolder, OutputFolder = OutputFolder };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex) { AppLogger.Instance.Warn($"[Settings] Error al guardar configuración: {ex.Message}"); }
        }
    }

    partial void OnRootFolderChanged(string value) => Save();

    partial void OnOutputFolderChanged(string value) => Save();

    private sealed class SettingsData
    {
        public string? RootFolder { get; set; }
        public string? OutputFolder { get; set; }
    }
}
