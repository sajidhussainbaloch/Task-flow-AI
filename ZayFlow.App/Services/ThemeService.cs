using System;
using System.Windows;
using Microsoft.Win32;

namespace ZayFlow.App.Services;

public enum AppTheme
{
    Light,
    Dark,
    System
}

public class ThemeService
{
    private AppTheme _currentThemeSetting = AppTheme.Dark;
    private bool _isDark = true;

    public event Action? ThemeChanged;

    public AppTheme CurrentThemeSetting
    {
        get => _currentThemeSetting;
        set
        {
            _currentThemeSetting = value;
            ApplyTheme();
        }
    }

    public bool IsDark => _isDark;

    public void Initialize()
    {
        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (_currentThemeSetting == AppTheme.System)
                ApplyTheme();
        };
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        _isDark = _currentThemeSetting switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            AppTheme.System => IsSystemDarkTheme(),
            _ => true
        };

        var themeUri = _isDark
            ? new Uri("pack://application:,,,/Themes/DarkTheme.xaml")
            : new Uri("pack://application:,,,/Themes/LightTheme.xaml");

        var app = Application.Current;
        if (app == null) return;

        // Remove existing theme dictionary (index 0 is theme, 1 is shared styles)
        var mergedDicts = app.Resources.MergedDictionaries;

        // Find and remove old theme
        ResourceDictionary? oldTheme = null;
        foreach (var dict in mergedDicts)
        {
            if (dict.Source != null &&
                (dict.Source.ToString().Contains("DarkTheme") || dict.Source.ToString().Contains("LightTheme")))
            {
                oldTheme = dict;
                break;
            }
        }

        if (oldTheme != null)
            mergedDicts.Remove(oldTheme);

        // Insert theme at position 0 (before shared styles)
        mergedDicts.Insert(0, new ResourceDictionary { Source = themeUri });

        ThemeChanged?.Invoke();
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intVal && intVal == 0;
        }
        catch
        {
            return true;
        }
    }
}
