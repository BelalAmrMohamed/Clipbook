using System;
using System.Linq;
using System.Windows;
using AdvancedClipboard.Models;
using AdvancedClipboard.Services;

namespace AdvancedClipboard;

public partial class SettingsWindow : Window
{
    private readonly HistoryManager _history;

    public SettingsWindow(HistoryManager history)
    {
        InitializeComponent();
        _history = history;
        Load(SettingsService.Current);
    }

    private void Load(AppSettings s)
    {
        PanelHotkeyBox.Text = s.PanelHotkey;
        FallbackHotkeyBox.Text = s.FallbackHotkey;
        PlainHotkeyBox.Text = s.PlainPasteHotkey;

        PinTop.IsChecked = s.PinPlacement == PinPlacement.Top;
        PinBottom.IsChecked = s.PinPlacement == PinPlacement.Bottom;
        PinTab.IsChecked = s.PinPlacement == PinPlacement.SeparateTab;
        AutoPinNotes.IsChecked = s.AutoPinNotes;

        MaxItemsBox.Text = s.MaxTempItems.ToString();
        MaxMemBox.Text = s.MaxTempMemoryMb.ToString();
        MaxAgeBox.Text = s.MaxTempAgeHours.ToString();
        UsageText.Text = $"Right now: {_history.TempCount()} temporary item(s) using {FormatBytes(_history.TempBytes())}, plus {_history.PinnedCount()} pinned.";

        MergeDupes.IsChecked = s.MergeDuplicates;
        ExplorerSave.IsChecked = s.ExplorerImageSave;
        StartWithWindows.IsChecked = s.StartWithWindows;
        ThemeSystem.IsChecked = s.Theme == ThemeMode.System;
        ThemeDark.IsChecked = s.Theme == ThemeMode.Dark;
        ThemeLight.IsChecked = s.Theme == ThemeMode.Light;

        EncryptBox.IsChecked = s.EncryptHistory;
        SkipFlagBox.IsChecked = s.SkipPasswordManagerFlag;
        SensOff.IsChecked = s.SensitiveMode == SensitiveMode.Off;
        SensSkip.IsChecked = s.SensitiveMode == SensitiveMode.Skip;
        SensWipe.IsChecked = s.SensitiveMode == SensitiveMode.AutoWipe;
        WipeSecondsBox.Text = s.SensitiveWipeSeconds.ToString();
        ExcludedBox.Text = string.Join(Environment.NewLine, s.ExcludedApps);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        if (!TryInt(MaxItemsBox.Text, out var maxItems) || !TryInt(MaxMemBox.Text, out var maxMem) ||
            !TryInt(MaxAgeBox.Text, out var maxAge) || !TryInt(WipeSecondsBox.Text, out var wipe))
        {
            ErrorText.Text = "Limits must be whole numbers (0 or more).";
            return;
        }
        if (!Hotkey.Parse(PanelHotkeyBox.Text).IsValid)
        {
            ErrorText.Text = "The main hotkey isn't valid. Example: Win+V";
            return;
        }
        foreach (var (text, name) in new[] { (FallbackHotkeyBox.Text, "fallback"), (PlainHotkeyBox.Text, "plain-text") })
        {
            if (!string.IsNullOrWhiteSpace(text) && !Hotkey.Parse(text).IsValid)
            {
                ErrorText.Text = $"The {name} hotkey isn't valid. Example: Ctrl+Shift+V";
                return;
            }
        }

        var s = new AppSettings
        {
            PanelHotkey = PanelHotkeyBox.Text.Trim(),
            FallbackHotkey = FallbackHotkeyBox.Text.Trim(),
            PlainPasteHotkey = PlainHotkeyBox.Text.Trim(),
            PinPlacement = PinBottom.IsChecked == true ? PinPlacement.Bottom : PinTab.IsChecked == true ? PinPlacement.SeparateTab : PinPlacement.Top,
            AutoPinNotes = AutoPinNotes.IsChecked == true,
            MaxTempItems = maxItems,
            MaxTempMemoryMb = maxMem,
            MaxTempAgeHours = maxAge,
            MergeDuplicates = MergeDupes.IsChecked == true,
            ExplorerImageSave = ExplorerSave.IsChecked == true,
            StartWithWindows = StartWithWindows.IsChecked == true,
            Theme = ThemeDark.IsChecked == true ? ThemeMode.Dark : ThemeLight.IsChecked == true ? ThemeMode.Light : ThemeMode.System,
            EncryptHistory = EncryptBox.IsChecked == true,
            SkipPasswordManagerFlag = SkipFlagBox.IsChecked == true,
            SensitiveMode = SensSkip.IsChecked == true ? SensitiveMode.Skip : SensWipe.IsChecked == true ? SensitiveMode.AutoWipe : SensitiveMode.Off,
            SensitiveWipeSeconds = wipe,
            ExcludedApps = ExcludedBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList()
        };

        SettingsService.Update(s);   // applies theme, hotkeys and limits via the Changed event
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static bool TryInt(string text, out int value) =>
        int.TryParse(text.Trim(), out value) && value >= 0;

    private static string FormatBytes(long bytes) =>
        bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1024.0 / 1024.0:0.#} MB";
}
