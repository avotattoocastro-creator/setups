using System;
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

    /// <summary>Minimum allowed AI analysis level.</summary>
    public const int MinAiLevel = 1;
    /// <summary>Maximum allowed AI analysis level.</summary>
    public const int MaxAiLevel = 5;

    /// <summary>
    /// AI analysis level for driving-behaviour and car-behaviour evaluation (1 = Basic → 5 = Professional).
    /// A higher level increases the number of setup proposals generated and the breadth of parameters analysed.
    /// </summary>
    [ObservableProperty]
    private int _aiLevel = MinAiLevel;

    partial void OnAiLevelChanged(int value)
    {
        if (value < MinAiLevel || value > MaxAiLevel)
            AiLevel = Math.Clamp(value, MinAiLevel, MaxAiLevel);
    }

    private SetupSettings() { }
}
