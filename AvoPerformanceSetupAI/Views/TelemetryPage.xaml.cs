using Microsoft.UI.Xaml.Controls;
using AvoPerformanceSetupAI.ViewModels;

namespace AvoPerformanceSetupAI.Views;

public sealed partial class TelemetryPage : Page
{
    public TelemetryViewModel ViewModel { get; } = new TelemetryViewModel();

    public TelemetryPage()
    {
        this.InitializeComponent();

        // Stop the polling timer when this page is unloaded (e.g. window closed)
        Unloaded += (_, _) => ViewModel.StopMonitoring();
    }
}
