using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ZayFlow.App.Converters;

public class BoolToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NotificationTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Services.NotificationType type) return new SolidColorBrush(Colors.Gray);
        return type switch
        {
            Services.NotificationType.Success => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399")),
            Services.NotificationType.Warning => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24")),
            Services.NotificationType.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171")),
            Services.NotificationType.Info => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA")),
            _ => new SolidColorBrush(Colors.Gray)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NotificationTypeToBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Services.NotificationType type) return new SolidColorBrush(Colors.Transparent);
        return type switch
        {
            Services.NotificationType.Success => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D2E23")),
            Services.NotificationType.Warning => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E2508")),
            Services.NotificationType.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E0D0D")),
            Services.NotificationType.Info => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D1A2E")),
            _ => new SolidColorBrush(Colors.Transparent)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SidebarWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
            return isExpanded ? new GridLength(240) : new GridLength(64);
        return new GridLength(240);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i) return i > 0;
        if (value is double d) return d > 0;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
