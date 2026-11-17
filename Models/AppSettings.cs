using System.Collections.Generic;

namespace AdvancedClipboard.Models;

public enum PinPlacement { Top, Bottom, SeparateTab }
public enum ThemeMode { System, Dark, Light }
public enum SensitiveMode { Off, Skip, AutoWipe }

public sealed class AppSettings
{
    // Hotkeys
    public string PanelHotkey { get; set; } = "Win+V";
    public string FallbackHotkey { get; set; } = "Ctrl+Shift+V";
    public string PlainPasteHotkey { get; set; } = "Ctrl+Alt+V";

    // General
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool StartWithWindows { get; set; } = true;

    // Pinned items + temporary history limits (0 = unlimited / off)
    public PinPlacement PinPlacement { get; set; } = PinPlacement.Top;
    public int MaxTempItems { get; set; } = 50;
    public int MaxTempMemoryMb { get; set; } = 200;
    public int MaxTempAgeHours { get; set; } = 0;

    // Behaviour
    public bool MergeDuplicates { get; set; } = true;
    public bool AutoPinNotes { get; set; } = true;
    public bool ExplorerImageSave { get; set; } = true;

    // Privacy
    public SensitiveMode SensitiveMode { get; set; } = SensitiveMode.AutoWipe;
    public int SensitiveWipeSeconds { get; set; } = 30;
    public bool SkipPasswordManagerFlag { get; set; } = true;
    public bool EncryptHistory { get; set; } = true;

    public List<string> ExcludedApps { get; set; } = new()
    {
        "KeePass", "KeePassXC", "1Password", "Bitwarden", "LastPass", "Dashlane", "NordPass", "Enpass"
    };
}
