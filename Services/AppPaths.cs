using System;
using System.IO;

namespace Clipbook.Services;

public static class AppPaths
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clipbook");

    public static string ImagesDir => Path.Combine(DataDir, "images");
    public static string DbPath => Path.Combine(DataDir, "history.db");
    public static string KeyPath => Path.Combine(DataDir, "key.bin");
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string LogPath => Path.Combine(DataDir, "error.log");

    /// <summary>One-time move of the data folder from the app's old name ("AdvancedClipboard") to "Clipbook".</summary>
    public static void MigrateLegacyData()
    {
        try
        {
            var old = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdvancedClipboard");
            if (Directory.Exists(old) && !Directory.Exists(DataDir))
                Directory.Move(old, DataDir);
        }
        catch (Exception ex) { Log("Legacy data migration failed: " + ex.Message); }
    }

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ImagesDir);
    }

    public static void Log(string message)
    {
        try
        {
            EnsureDirs();
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }
}
