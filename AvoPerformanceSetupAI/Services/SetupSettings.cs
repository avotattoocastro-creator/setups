using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
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
    private const string KeyUiScale      = "UiScale";

    private readonly bool _hasPackageIdentity;

    public static SetupSettings Instance { get; } = new SetupSettings();

    [ObservableProperty]
    private string _rootFolder = string.Empty;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private double _uiScale = 1.0;

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
            _uiScale      = local.Values[KeyUiScale] is double d ? d : 1.0;
        }
        catch (InvalidOperationException)
        {
            // Happens when running unpackaged (no package identity). Keep defaults and disable persistence.
            _hasPackageIdentity = false;
            _rootFolder  = string.Empty;
            _outputFolder = string.Empty;
            _uiScale     = 1.0;
        }
    }

    partial void OnRootFolderChanged(string value)
    {
        if (_hasPackageIdentity)
            ApplicationData.Current.LocalSettings.Values[KeyRootFolder] = value;
    }

    partial void OnOutputFolderChanged(string value)
    {
        if (_hasPackageIdentity)
            ApplicationData.Current.LocalSettings.Values[KeyOutputFolder] = value;
    }

    partial void OnUiScaleChanged(double value)
    {
        if (_hasPackageIdentity)
            ApplicationData.Current.LocalSettings.Values[KeyUiScale] = value;

        // Push the new value into the live resource dictionary so converters
        // pick it up on the next page navigation / resource lookup.
        // If Application.Current is null (e.g. during early init or unit tests)
        // the live update is silently skipped; the persisted value is loaded
        // correctly on the next app launch via OnLaunched in App.xaml.cs.
        if (Application.Current?.Resources is { } res)
            res["UiScale"] = value;
    }
}
