using CommunityToolkit.Mvvm.ComponentModel;
using Windows.Storage;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// Application-wide settings singleton.
/// Holds the root folder path for setup files so all pages can observe it.
/// Settings are persisted to ApplicationData.Current.LocalSettings so they
/// survive application restarts.
/// </summary>
public sealed partial class SetupSettings : ObservableObject
{
    private const string RootFolderKey = "RootFolder";
    private const string OutputFolderKey = "OutputFolder";

    public static SetupSettings Instance { get; } = new SetupSettings();

    [ObservableProperty]
    private string _rootFolder = string.Empty;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    private SetupSettings()
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        RootFolder = localSettings.Values[RootFolderKey] as string ?? string.Empty;
        OutputFolder = localSettings.Values[OutputFolderKey] as string ?? string.Empty;
    }

    partial void OnRootFolderChanged(string value)
    {
        ApplicationData.Current.LocalSettings.Values[RootFolderKey] = value;
    }

    partial void OnOutputFolderChanged(string value)
    {
        ApplicationData.Current.LocalSettings.Values[OutputFolderKey] = value;
    }
}
