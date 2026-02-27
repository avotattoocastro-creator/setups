using System;
using System.Collections.Specialized;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.ViewModels;

namespace AvoPerformanceSetupAI.Views;

// ── Value converters ─────────────────────────────────────────────────────────

/// <summary>Maps LogCategory → badge background brush.</summary>
public sealed class CategoryToBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (LogCategory)value switch
        {
            LogCategory.AI    => new SolidColorBrush(Windows.UI.Color.FromArgb(255,  0, 180, 180)), // teal
            LogCategory.DATA  => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 64, 128, 200)), // blue
            LogCategory.WARN  => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 160,   0)), // amber
            LogCategory.ERROR => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210,  55,  55)), // red
            _                 => new SolidColorBrush(Windows.UI.Color.FromArgb(255,  96, 128, 128)), // gray (INFO)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Maps LogCategory → text foreground brush.</summary>
public sealed class CategoryToTextBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (LogCategory)value switch
        {
            LogCategory.AI    => new SolidColorBrush(Windows.UI.Color.FromArgb(255,   0, 210, 210)),
            LogCategory.DATA  => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 170, 240)),
            LogCategory.WARN  => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 190,  40)),
            LogCategory.ERROR => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240,  90,  90)),
            _                 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 220, 220)),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

// ── Page ─────────────────────────────────────────────────────────────────────

public sealed partial class TerminalPage : Page
{
    public TerminalViewModel ViewModel { get; } = new TerminalViewModel();

    public TerminalPage()
    {
        this.InitializeComponent();

        // Register converters as page resources so the DataTemplate can find them
        Resources["CategoryToBadgeBrushConverter"] = new CategoryToBadgeBrushConverter();
        Resources["CategoryToTextBrushConverter"]  = new CategoryToTextBrushConverter();

        // Register filter-pill style
        Resources["FilterPillStyle"] = BuildFilterPillStyle();

        // React to collection changes for auto-scroll and counter
        ViewModel.FilteredEntries.CollectionChanged += OnFilteredEntriesChanged;
        UpdateEntryCount();
    }

    // ── Filter pill style ────────────────────────────────────────────────────

    private static Style BuildFilterPillStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 42, 42))));
        style.Setters.Add(new Setter(Button.ForegroundProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 138, 171, 171))));
        style.Setters.Add(new Setter(Button.BorderBrushProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 46, 64, 64))));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Button.CornerRadiusProperty, new CornerRadius(12)));
        style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 3, 10, 3)));
        style.Setters.Add(new Setter(Button.FontSizeProperty, 11.0));
        style.Setters.Add(new Setter(Button.FontFamilyProperty, new FontFamily("Consolas")));
        return style;
    }

    // ── Filter button clicks ─────────────────────────────────────────────────

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            ViewModel.SetFilterCommand.Execute(tag);
            UpdateStatusBar(tag);
        }
    }

    private void UpdateStatusBar(string filter)
    {
        StatusBarText.Text = string.IsNullOrEmpty(filter)
            ? "● En línea  |  Mostrando todos los eventos"
            : $"● En línea  |  Filtro activo: {filter}";
        UpdateEntryCount();
    }

    // ── Auto-scroll + counter ────────────────────────────────────────────────

    private void OnFilteredEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEntryCount();

        // Auto-scroll to bottom when new items are added
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // Defer to let the layout pass complete before scrolling
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null));
        }
    }

    private void UpdateEntryCount()
    {
        int count = ViewModel.FilteredEntries.Count;
        EntryCountText.Text = $"{count} entrada{(count == 1 ? "" : "s")}";
    }
}
