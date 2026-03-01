using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AvoPerformanceSetupAI.Services;

namespace AvoPerformanceSetupAI.Controls;

public sealed partial class StartupSplashOverlay : UserControl
{
    private const int MinVisibleMs = 2500;
    private DateTime _shownAt = DateTime.MinValue;

    // ── IsOpen ───────────────────────────────────────────────────────────────

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(
            nameof(IsOpen), typeof(bool), typeof(StartupSplashOverlay),
            new PropertyMetadata(true, OnIsOpenChanged));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (StartupSplashOverlay)d;
        bool isOpen = (bool)e.NewValue;
        ctrl.RootOverlay.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        if (isOpen)
        {
            ctrl.RootOverlay.Opacity = 1.0;
            ctrl._shownAt = DateTime.UtcNow;
            ctrl.ContentEntranceStoryboard.Begin();
        }
    }

    // ── StatusText ────────────────────────────────────────────────────────────

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(
            nameof(StatusText), typeof(string), typeof(StartupSplashOverlay),
            new PropertyMetadata("Inicializando módulos...", OnStatusTextChanged));

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    private static void OnStatusTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (StartupSplashOverlay)d;
        ctrl.StatusTextBlock.Text = (string)(e.NewValue ?? string.Empty);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    public StartupSplashOverlay()
    {
        this.InitializeComponent();
        StatusTextBlock.Text = StatusText;
        // Default IsOpen=true: DependencyProperty callbacks don't fire for the
        // initial default value, so we track _shownAt here for the common case.
        // OnIsOpenChanged handles subsequent explicit IsOpen=true assignments.
        _shownAt = DateTime.UtcNow;
        Loaded += (_, _) => ContentEntranceStoryboard.Begin();
    }

    // ── CloseAsync ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the overlay has been visible for at least <see cref="MinVisibleMs"/> ms,
    /// then fades it out over 600 ms and collapses it.
    /// Safe to call from the UI thread.
    /// </summary>
    public async Task CloseAsync()
    {
        if (_shownAt != DateTime.MinValue)
        {
            var elapsed   = DateTime.UtcNow - _shownAt;
            var remaining = TimeSpan.FromMilliseconds(MinVisibleMs) - elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining);
        }

        var tcs = new TaskCompletionSource<bool>();

        void Completed(object? s, object e)
        {
            tcs.TrySetResult(true);
        }

        FadeOutStoryboard.Completed += Completed;
        FadeOutStoryboard.Begin();
        await tcs.Task;
        FadeOutStoryboard.Completed -= Completed;

        IsOpen = false;
    }

    // ── CloseWhenReadyAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Awaits <paramref name="initTask"/>, then ensures the minimum visible
    /// duration before fading out and collapsing the overlay.
    /// Errors from <paramref name="initTask"/> are swallowed so the overlay
    /// always closes even if initialisation fails.
    /// </summary>
    public async Task CloseWhenReadyAsync(Func<Task> initTask)
    {
        try
        {
            await initTask();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Info($"Splash init error: {ex.Message}");
        }

        await CloseAsync();
    }
}
