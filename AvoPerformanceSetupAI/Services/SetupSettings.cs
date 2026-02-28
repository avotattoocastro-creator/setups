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

    private readonly bool _hasPackageIdentity;

    public static SetupSettings Instance { get; } = new SetupSettings();

    [ObservableProperty]
    private string _rootFolder = string.Empty;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    private SetupSettings()
    {
        try
        {
            var local = ApplicationData.Current.LocalSettings;
            _hasPackageIdentity = true;

            // Assign backing fields directly to avoid writing the loaded values back to
            // LocalSettings (which would happen if we used the property setters).
            _rootFolder   = local.Values[KeyRootFolder]   as string ?? string.Empty;
            _outputFolder = local.Values[KeyOutputFolder] as string ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            // Happens when running unpackaged (no package identity). Keep defaults and disable persistence.
            _hasPackageIdentity = false;
            _rootFolder = string.Empty;
            _outputFolder = string.Empty;
        }
    }

    partial void OnRootFolderChanged(string value)
    {
        if (_hasPackageIdentity)
        {
            ApplicationData.Current.LocalSettings.Values[KeyRootFolder] = value;
        }
    }

    partial void OnOutputFolderChanged(string value)
    {
        if (_hasPackageIdentity)
        {
            ApplicationData.Current.LocalSettings.Values[KeyOutputFolder] = value;
        }
    }
}
