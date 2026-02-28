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

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    /// <summary>
    /// Full path to the currently selected setup <c>.ini</c> file.
    /// Set by <c>SessionsViewModel</c> and consumed by <c>AiAssistantViewModel</c>.
    /// </summary>
    [ObservableProperty]
    private string _currentSetupPath = string.Empty;

    private SetupSettings() { }
}
