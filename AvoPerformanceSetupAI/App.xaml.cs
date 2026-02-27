using Microsoft.UI.Xaml;

namespace AvoPerformanceSetupAI;

public partial class App : Application
{
    private Window? _window;

    /// <summary>Exposed so pages can obtain the HWND for native dialogs (e.g. FolderPicker).</summary>
    public Window? MainWindow => _window;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
