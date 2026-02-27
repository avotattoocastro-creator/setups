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

        // Show current folder (if already set)
        FolderPathBox.Text = SetupSettings.Instance.RootFolder;

        // Keep the textbox in sync if the setting changes from elsewhere
        SetupSettings.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupSettings.RootFolder))
                FolderPathBox.Text = SetupSettings.Instance.RootFolder;
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
}
