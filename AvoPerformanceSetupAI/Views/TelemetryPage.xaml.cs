using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
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

        // Switch between NormalMode and RaceViewMode when IsRaceViewActive changes.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TelemetryViewModel.IsRaceViewActive))
                ApplyRaceViewState(animated: true);
        };

        Unloaded += (_, _) => ViewModel.Dispose();

        // Apply the persisted initial state (no animation on first load).
        Loaded += (_, _) => ApplyRaceViewState(animated: false);

        // TAB key → show HUD quick overlay (handled before focus navigation).
        var tabKey = new KeyboardAccelerator { Key = VirtualKey.Tab };
        tabKey.Invoked += OnTabHudInvoked;
        KeyboardAccelerators.Add(tabKey);
    }

    // ── Race View helpers ─────────────────────────────────────────────────────

    private void ApplyRaceViewState(bool animated)
    {
        var state = ViewModel.IsRaceViewActive ? "RaceViewMode" : "NormalMode";
        VisualStateManager.GoToState(this, state, animated);
    }

    // ── HUD Quick Overlay ─────────────────────────────────────────────────────

    private void OnTabHudInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;  // prevent default focus-navigation behaviour
        ShowHud();
    }

    private void ShowHud()
    {
        var vm = ViewModel;

        var line1 = $"{vm.StatusText}  •  {vm.ConnectionStatusText}";

        var line2 = $"DELTA: {vm.LapDelta}  Vuelta: {vm.LapTimeReal}  Ideal: {vm.LapTimeIdeal}";

        var line3 = vm.Channels.Count > 4
            ? $"{vm.Channels[0].RealDisplay} km/h  •  G {vm.Channels[2].RealDisplay}  •  {vm.Channels[1].RealDisplay} rpm"
            : string.Empty;

        var line4 = vm.Proposals.Count > 0
            ? $"▶ [{vm.Proposals[0].Section}] {vm.Proposals[0].Parameter} {vm.Proposals[0].Delta}  {vm.Proposals[0].Reason}"
            : "Sin propuestas activas";

        var line5 = $"FASE: {vm.CurrentPhase}  •  Curva: {vm.LastCornerDirection} {vm.LastCornerDurationText}";

        _ = HudOverlay.ShowAsync(line1, line2, line3, line4, line5);
    }

    private void AutoScroll(ScrollViewer sv) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
            () => sv.ChangeView(null, sv.ScrollableHeight, null));

    // ── Test Combo click handler ──────────────────────────────────────────────

    /// <summary>
    /// Called when the "Test Combo" button inside a <see cref="CombinedProposal"/>
    /// row is clicked. The combo is stored in <c>Button.Tag</c> so we can pass it
    /// to <see cref="TelemetryViewModel.TestComboCommand"/> without a complex
    /// nested binding.
    /// </summary>
    private void OnTestComboClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button btn &&
            btn.Tag is AvoPerformanceSetupAI.Telemetry.CombinedProposal combo)
            ViewModel.TestComboCommand.Execute(combo);
    }

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

