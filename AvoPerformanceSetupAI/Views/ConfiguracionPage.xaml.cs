using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage.Pickers;
using AvoPerformanceSetupAI.Services;

namespace AvoPerformanceSetupAI.Views;

public sealed partial class ConfiguracionPage : Page
{
    /// <summary>
    /// Must match <c>StepFrequency</c> on <see cref="UiScaleSlider"/> in the XAML.
    /// </summary>
    private const double UiScaleStep = 0.05;
    public ConfiguracionPage()
    {
        this.InitializeComponent();

        // Show current folders (if already set)
        FolderPathBox.Text = SetupSettings.Instance.RootFolder;
        OutputFolderPathBox.Text = SetupSettings.Instance.OutputFolder;

        // Keep the textboxes in sync if the settings change from elsewhere
        SetupSettings.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupSettings.RootFolder))
                FolderPathBox.Text = SetupSettings.Instance.RootFolder;
            else if (e.PropertyName == nameof(SetupSettings.OutputFolder))
                OutputFolderPathBox.Text = SetupSettings.Instance.OutputFolder;
        };

        // Initialise the UI scale slider from persisted setting
        UiScaleSlider.Value = SetupSettings.Instance.UiScale;
        UiScaleValueText.Text = $"{SetupSettings.Instance.UiScale:F2}×";

        // Initialise branding controls from persisted settings
        WatermarkEnabledToggle.IsOn = SetupSettings.Instance.BrandWatermarkEnabled;
        WatermarkOpacitySlider.Value = SetupSettings.Instance.BrandWatermarkOpacity;
        WatermarkOpacityValueText.Text = $"{SetupSettings.Instance.BrandWatermarkOpacity * 100:F0}%";
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

    private void UiScaleSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Round to the nearest step to avoid floating-point noise
        double v = Math.Round(e.NewValue / UiScaleStep) * UiScaleStep;
        SetupSettings.Instance.UiScale = v;
        if (UiScaleValueText is not null)
            UiScaleValueText.Text = $"{v:F2}×";
    }

    private void WatermarkToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SetupSettings.Instance.BrandWatermarkEnabled = WatermarkEnabledToggle.IsOn;
    }

    private void WatermarkOpacity_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        const double step = 0.01;
        double v = Math.Round(e.NewValue / step) * step;
        SetupSettings.Instance.BrandWatermarkOpacity = v;
        if (WatermarkOpacityValueText is not null)
            WatermarkOpacityValueText.Text = $"{v * 100:F0}%";
    }
}
