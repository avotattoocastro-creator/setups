using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using AvoPerformanceSetupAI.ViewModels;

namespace AvoPerformanceSetupAI.Views;

public sealed partial class TelemetryPage : Page
{
    public TelemetryViewModel ViewModel { get; } = new TelemetryViewModel();

    public TelemetryPage()
    {
        this.InitializeComponent();

        // Pass the UI dispatcher so the timer callbacks can marshal to the UI thread
        ViewModel.Initialize(DispatcherQueue);

        // Auto-scroll each analysis terminal when a new entry is appended
        ViewModel.BehaviorLogs.CollectionChanged += (_, _) => AutoScroll(BehaviorScrollViewer);
        ViewModel.DrivingLogs.CollectionChanged  += (_, _) => AutoScroll(DrivingScrollViewer);
        ViewModel.SetupLogs.CollectionChanged    += (_, _) => AutoScroll(SetupScrollViewer);
    }

    private void AutoScroll(ScrollViewer sv) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
            () => sv.ChangeView(null, sv.ScrollableHeight, null));
}
