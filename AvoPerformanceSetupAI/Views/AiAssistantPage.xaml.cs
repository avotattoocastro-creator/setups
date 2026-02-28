using System;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.ViewModels;

namespace AvoPerformanceSetupAI.Views;

// ── Value converters ──────────────────────────────────────────────────────────

/// <summary>Maps MessageRole → bubble background brush.</summary>
public sealed class RoleToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (MessageRole)value == MessageRole.User
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 40, 50))   // user: dark blue-teal
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 30, 30));  // assistant: near-black

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Maps MessageRole → sender label text ("Tú" or "IA").</summary>
public sealed class RoleToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (MessageRole)value == MessageRole.User ? "Tú" : "IA";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Maps MessageRole → sender label foreground brush.</summary>
public sealed class RoleToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (MessageRole)value == MessageRole.User
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255,  96, 200, 200)) // user: teal
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255,   0, 212, 180)); // assistant: bright teal

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

// ── Page ──────────────────────────────────────────────────────────────────────

public sealed partial class AiAssistantPage : Page
{
    public AiAssistantViewModel ViewModel { get; } = new AiAssistantViewModel();

    public AiAssistantPage()
    {
        this.InitializeComponent();

        // Register converters so the DataTemplate x:Bind can resolve them
        Resources["RoleToBackgroundConverter"] = new RoleToBackgroundConverter();
        Resources["RoleToLabelConverter"]      = new RoleToLabelConverter();
        Resources["RoleToForegroundConverter"] = new RoleToForegroundConverter();

        // Auto-scroll chat when new messages arrive
        ViewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    // ── Keyboard shortcut: Enter sends the message ────────────────────────────

    private void InputBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter &&
            ViewModel.SendCommand.CanExecute(null))
        {
            ViewModel.SendCommand.Execute(null);
        }
    }

    // ── Auto-scroll to bottom on new message ─────────────────────────────────

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null));
        }
    }
}
