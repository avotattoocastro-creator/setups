using CommunityToolkit.Mvvm.ComponentModel;
using Windows.Storage;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// Application-wide settings singleton.
/// Holds the root folder path for setup files so all pages can observe it.
/// Values are persisted to <see cref="ApplicationData.Current.LocalSettings"/> so they
/// survive application restarts.
/// </summary>
public sealed partial class SetupSettings : ObservableObject
{
    private const string KeyRootFolder   = "RootFolder";
    private const string KeyOutputFolder = "OutputFolder";

    public static SetupSettings Instance { get; } = new SetupSettings();

    [ObservableProperty]
    private string _rootFolder = string.Empty;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    private SetupSettings()
    {
        var local = ApplicationData.Current.LocalSettings;
        // Assign backing fields directly to avoid writing the loaded values back to
        // LocalSettings (which would happen if we used the property setters).
        _rootFolder   = local.Values[KeyRootFolder]   as string ?? string.Empty;
        _outputFolder = local.Values[KeyOutputFolder] as string ?? string.Empty;
    }

    partial void OnRootFolderChanged(string value) =>
        ApplicationData.Current.LocalSettings.Values[KeyRootFolder] = value;

    partial void OnOutputFolderChanged(string value) =>
        ApplicationData.Current.LocalSettings.Values[KeyOutputFolder] = value;
}
