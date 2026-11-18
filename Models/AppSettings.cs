using System.Collections.Generic;

namespace Clipbook.Models;

public enum PinPlacement { Top, Bottom, SeparateTab }
public enum ThemeMode { System, Dark, Light }
public enum SensitiveMode { Off, Skip, AutoWipe }

public sealed class AppSettings
{
    // Hotkeys
    public string PanelHotkey { get; set; } = "Win+V";
    public string FallbackHotkey { get; set; } = "Ctrl+Shift+V";
    public string PlainPasteHotkey { get; set; } = "Ctrl+Alt+V";
    public string QueueHotkey { get; set; } = "Ctrl+Alt+B";              // paste the next item from the queue
    public string QueueToggleHotkey { get; set; } = "Ctrl+Alt+Shift+B"; // start/stop collecting copies into the queue

    // General
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool StartWithWindows { get; set; } = true;

    // Pinned items + temporary history limits (0 = unlimited / off)
    public PinPlacement PinPlacement { get; set; } = PinPlacement.Top;
    public int MaxTempItems { get; set; } = 50;
    public int MaxTempMemoryMb { get; set; } = 200;
    public int MaxTempAgeHours { get; set; } = 0;

    // Panel size (remembered when you resize the panel)
    public double PanelWidth { get; set; } = 400;
    public double PanelHeight { get; set; } = 540;

    // Behaviour
    public bool MergeDuplicates { get; set; } = true;
    public bool AutoPinNotes { get; set; } = false;
    public bool ExplorerImageSave { get; set; } = true;

    // Link previews contact the website of each copied link, so they are opt-in.
    public bool FetchLinkTitles { get; set; } = false;

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
