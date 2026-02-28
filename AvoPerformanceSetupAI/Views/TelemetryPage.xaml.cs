using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
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
        ViewModel.CornerLogs.CollectionChanged   += (_, _) => AutoScroll(CornerScrollViewer);

        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private void AutoScroll(ScrollViewer sv) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
            () => sv.ChangeView(null, sv.ScrollableHeight, null));

    // ── CSV import ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a <see cref="FileOpenPicker"/> for CSV files (WinUI 3 requires the
    /// window handle to be set before showing the picker) and, if the user picks
    /// a file, delegates the import to <see cref="TelemetryViewModel.ImportCsvFileAsync"/>.
    /// </summary>
    private async void OnImportCsvClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode               = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".txt");

        // WinUI 3 requires the HWND of the owning window
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            (Application.Current as App)?.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        await ViewModel.ImportCsvFileAsync(file.Path);
    }
}

