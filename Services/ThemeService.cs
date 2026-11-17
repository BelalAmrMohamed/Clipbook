using System;
using System.Windows;
using AdvancedClipboard.Models;
using Microsoft.Win32;

namespace AdvancedClipboard.Services;

public static class ThemeService
{
    public static bool IsDark { get; private set; } = true;

    public static void Apply(ThemeMode mode)
    {
        IsDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => WindowsPrefersDark()
        };

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
        };
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict; else merged.Insert(0, dict);
    }

    private static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v ? v == 0 : true;
        }
        catch { return true; }
    }
}
