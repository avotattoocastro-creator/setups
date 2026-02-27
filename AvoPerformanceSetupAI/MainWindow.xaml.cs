using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using AvoPerformanceSetupAI.Services;

namespace AvoPerformanceSetupAI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        AppLogger.Instance.Initialize(DispatcherQueue);
        AppLogger.Instance.Info("AvoPerformanceSetupAI iniciado correctamente.");
        AppLogger.Instance.Info("Motor de UI: WinUI 3 / Windows App SDK 1.5");
        SetupWindow();
    }

    private void SetupWindow()
    {
        // Set window size
        var appWindow = this.AppWindow;
        appWindow.Resize(new SizeInt32(1280, 820));
        appWindow.Title = "AvoPerformanceSetupAI";

        // Custom title bar
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 224, 240, 240);
            titleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 10, 20, 20);
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarGrid);

        // Request dark theme
        if (Content is FrameworkElement fe)
        {
            fe.RequestedTheme = ElementTheme.Dark;
        }
    }

    private void MainTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Tab changed - can add logic here
    }
}
