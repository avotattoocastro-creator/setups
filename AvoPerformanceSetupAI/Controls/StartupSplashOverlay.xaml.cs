using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AvoPerformanceSetupAI.Controls;

public sealed partial class StartupSplashOverlay : UserControl
{
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
        ctrl.RootOverlay.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        if ((bool)e.NewValue)
        {
            ctrl.RootOverlay.Opacity = 1.0;
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
    }

    // ── CloseAsync ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fades the overlay out over 350 ms then collapses it.
    /// Safe to call from the UI thread.
    /// </summary>
    public async Task CloseAsync()
    {
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
}
