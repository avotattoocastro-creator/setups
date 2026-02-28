using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using AvoPerformanceSetupAI.Models;

namespace AvoPerformanceSetupAI.Converters;

/// <summary>Maps <see cref="LogCategory"/> → badge background brush.</summary>
public sealed class CategoryToBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (LogCategory)value switch
        {
            LogCategory.AI    => new SolidColorBrush(Windows.UI.Color.FromArgb(255,   0, 180, 180)), // teal
            LogCategory.DATA  => new SolidColorBrush(Windows.UI.Color.FromArgb(255,  64, 128, 200)), // blue
            LogCategory.WARN  => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 160,   0)), // amber
            LogCategory.ERROR => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210,  55,  55)), // red
            _                 => new SolidColorBrush(Windows.UI.Color.FromArgb(255,  96, 128, 128)), // gray (INFO)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Maps <see cref="LogCategory"/> → text foreground brush.</summary>
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

/// <summary>Formats a <see cref="float"/> (0..1) as a percentage string, e.g. "73%".</summary>
public sealed class FloatToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is float f ? $"{f:P0}" : "0%";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Inverts a <see cref="bool"/> value — used to enable controls only when not simulating.</summary>
public sealed class NotBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : false;
}
