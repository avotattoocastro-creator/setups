using CommunityToolkit.Mvvm.ComponentModel;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// Application-wide settings singleton.
/// Holds the root folder path for setup files so all pages can observe it.
/// </summary>
public sealed partial class SetupSettings : ObservableObject
{
    public static SetupSettings Instance { get; } = new SetupSettings();

    [ObservableProperty]
    private string _rootFolder = string.Empty;

    private SetupSettings() { }
}
