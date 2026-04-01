using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ZayFlow.App.Converters;

/// <summary>
/// Converts a boolean value to its inverse.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>
/// Converts a boolean value to Visibility.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>
/// Theme background converter (true=dark #1E1E2E, false=light #F5F5F5).
/// </summary>
public class ThemeBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool?)value == true 
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Theme foreground converter (true=light text, false=dark text).
/// </summary>
public class ThemeForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool?)value == true ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Theme status bar background converter.
/// </summary>
public class ThemeStatusBarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool?)value == true
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3C"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8E8E8"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Theme input background converter.
/// </summary>
public class ThemeInputConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool?)value == true
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3C"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Theme ListBox background converter.
/// </summary>
public class ThemeListBoxConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool?)value == true
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252535"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAFAFA"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts boolean online status to status indicator color (green=online, red=offline).
/// </summary>
public class StatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool?)value == true
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6BCF7F"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts dark mode boolean to emoji icon (☀️ for dark mode on, 🌙 for dark mode off).
/// </summary>
public class DarkModeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool?)value == true ? "\uE706" : "\uE708";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a hex color string (e.g., "#FF6B6B") to a SolidColorBrush.
/// </summary>
public class HexColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hexColor)
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            }
            catch
            {
                return new SolidColorBrush(Colors.White);
            }
        }
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Maps risk level text to consistent badge colors.
/// </summary>
public class RiskLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var risk = value?.ToString()?.Trim().ToLowerInvariant();

        return risk switch
        {
            "high" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0392B")),
            "medium" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E67E22")),
            "low" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E8449")),
            "info" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D8CFF")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5D6D7E"))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
