using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clipbook.Models;
using Microsoft.Win32;

namespace Clipbook.Services;

public static class SettingsService
{
    public static AppSettings Current { get; private set; } = new();
    public static event Action? Changed;

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Load()
    {
        try
        {
            AppPaths.EnsureDirs();
            if (File.Exists(AppPaths.SettingsPath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath), Opts) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppPaths.Log("Settings load failed: " + ex.Message);
            Current = new AppSettings();
        }
        Normalize(Current);
    }

    public static void Update(AppSettings settings)
    {
        Normalize(settings);
        Current = settings;
        Save();
        StartupService.Set(settings.StartWithWindows);
        Changed?.Invoke();
    }

    public static void Save()
    {
        try
        {
            AppPaths.EnsureDirs();
            File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(Current, Opts));
        }
        catch (Exception ex) { AppPaths.Log("Settings save failed: " + ex.Message); }
    }

    private static void Normalize(AppSettings s)
    {
        s.MaxTempItems = Math.Max(0, s.MaxTempItems);
        s.MaxTempMemoryMb = Math.Max(0, s.MaxTempMemoryMb);
        s.MaxTempAgeHours = Math.Max(0, s.MaxTempAgeHours);
        s.SensitiveWipeSeconds = Math.Max(5, s.SensitiveWipeSeconds);
        s.PanelHotkey = (s.PanelHotkey ?? "").Trim();
        s.FallbackHotkey = (s.FallbackHotkey ?? "").Trim();
        s.PlainPasteHotkey = (s.PlainPasteHotkey ?? "").Trim();
        s.ExcludedApps = (s.ExcludedApps ?? new())
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Clipbook";

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            key.DeleteValue("AdvancedClipboard", throwOnMissingValue: false);   // old name of this app
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, "\"" + exe + "\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) { AppPaths.Log("Startup registry update failed: " + ex.Message); }
    }
}
