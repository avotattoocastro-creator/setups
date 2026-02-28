using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using AvoPerformanceSetupAI.Services;

namespace AvoPerformanceSetupAI.Views;

public sealed partial class ConfiguracionPage : Page
{
    public ConfiguracionPage()
    {
        this.InitializeComponent();

        // Show current folders (if already set)
        FolderPathBox.Text = SetupSettings.Instance.RootFolder;
        OutputFolderPathBox.Text = SetupSettings.Instance.OutputFolder;

        // Initialise AI level selector
        AiLevelComboBox.SelectedIndex = Math.Clamp(SetupSettings.Instance.AiLevel,
            SetupSettings.MinAiLevel, SetupSettings.MaxAiLevel) - 1;

        // Keep the textboxes in sync if the settings change from elsewhere
        SetupSettings.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupSettings.RootFolder))
                FolderPathBox.Text = SetupSettings.Instance.RootFolder;
            else if (e.PropertyName == nameof(SetupSettings.OutputFolder))
                OutputFolderPathBox.Text = SetupSettings.Instance.OutputFolder;
            else if (e.PropertyName == nameof(SetupSettings.AiLevel))
                AiLevelComboBox.SelectedIndex = Math.Clamp(SetupSettings.Instance.AiLevel,
                    SetupSettings.MinAiLevel, SetupSettings.MaxAiLevel) - 1;
        };
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");

        // WinUI 3 requires initializing the picker with the window handle
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            (Application.Current as App)!.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            SetupSettings.Instance.RootFolder = folder.Path;
            AppLogger.Instance.Info($"Carpeta de setups configurada: {folder.Path}");
        }
        else
        {
            AppLogger.Instance.Info("Selección de carpeta cancelada por el usuario.");
        }
    }

    private async void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            (Application.Current as App)!.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            SetupSettings.Instance.OutputFolder = folder.Path;
            AppLogger.Instance.Info($"Carpeta de destino configurada: {folder.Path}");
        }
        else
        {
            AppLogger.Instance.Info("Selección de carpeta de destino cancelada por el usuario.");
        }
    }

    private void AiLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var newLevel = AiLevelComboBox.SelectedIndex + 1;
        if (newLevel != SetupSettings.Instance.AiLevel)
        {
            SetupSettings.Instance.AiLevel = newLevel;
            AppLogger.Instance.Ai($"Nivel de IA de conducción cambiado a {newLevel}.");
        }
    }
}
