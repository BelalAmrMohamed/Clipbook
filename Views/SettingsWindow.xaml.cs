using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clipbook.Models;
using Clipbook.Services;

namespace Clipbook;

public partial class SettingsWindow : Window
{
    private readonly HistoryManager _history;

    public SettingsWindow(HistoryManager history)
    {
        InitializeComponent();
        _history = history;
        Load(SettingsService.Current);
        _loaded = true;
        Validate();
    }

    private bool _loaded;

    private void Load(AppSettings s)
    {
        PanelHotkeyBox.Text = s.PanelHotkey;
        FallbackHotkeyBox.Text = s.FallbackHotkey;
        PlainHotkeyBox.Text = s.PlainPasteHotkey;
        QueueHotkeyBox.Text = s.QueueHotkey;
        QueueToggleHotkeyBox.Text = s.QueueToggleHotkey;
        FetchLinksBox.IsChecked = s.FetchLinkTitles;

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

    // ---------------- Live validation ----------------

    private void Field_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loaded) Validate();
    }

    /// <summary>Checks every field, shows/hides the red messages, and enables Save only when all are valid.</summary>
    private bool Validate()
    {
        bool ok = true;

        ok &= Show(MaxItemsBox, MaxItemsError, IntError(MaxItemsBox.Text, 0, 1_000_000, " (0 = no limit)"));
        ok &= Show(MaxMemBox, MaxMemError, IntError(MaxMemBox.Text, 0, 1_000_000, " (0 = no limit)"));
        ok &= Show(MaxAgeBox, MaxAgeError, IntError(MaxAgeBox.Text, 0, 87_600, " (0 = no limit)"));
        ok &= Show(WipeSecondsBox, WipeError, IntError(WipeSecondsBox.Text, 5, 86_400, ""));

        // Hotkeys: the main one is required, the others are optional. No two may be the same.
        var fields = new (TextBox Box, TextBlock Label, string Name, bool Required)[]
        {
            (PanelHotkeyBox, PanelHotkeyError, "the main hotkey", true),
            (FallbackHotkeyBox, FallbackHotkeyError, "the fallback hotkey", false),
            (PlainHotkeyBox, PlainHotkeyError, "the plain-text hotkey", false),
            (QueueHotkeyBox, QueueHotkeyError, "the queue paste hotkey", false),
            (QueueToggleHotkeyBox, QueueToggleHotkeyError, "the queue collecting hotkey", false),
        };
        var seen = new System.Collections.Generic.Dictionary<Hotkey, string>();
        foreach (var f in fields)
        {
            string? err = null;
            if (!f.Required && string.IsNullOrWhiteSpace(f.Box.Text))
            {
                // empty = disabled
            }
            else if (!Hotkey.TryParseStrict(f.Box.Text, out var hk, out var parseError))
            {
                err = parseError;
            }
            else if (seen.TryGetValue(hk, out var other))
            {
                err = $"Same as {other}. Pick a different one" + (f.Required ? "." : ", or leave this empty.");
            }
            else
            {
                seen[hk] = f.Name;
            }
            ok &= Show(f.Box, f.Label, err);
        }

        SaveBtn.IsEnabled = ok;
        ErrorText.Text = ok ? "" : "Fix the highlighted fields to save.";
        return ok;
    }

    private bool Show(TextBox box, TextBlock label, string? message)
    {
        bool valid = message == null;
        label.Text = message ?? "";
        label.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (valid) box.ClearValue(Control.BorderBrushProperty);
        else box.BorderBrush = (Brush)FindResource("DangerBrush");
        return valid;
    }

    private static string? IntError(string raw, int min, int max, string zeroHint)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0) return "Enter a number" + zeroHint + ".";
        if (t.Length > 1 && t[0] == '-' && t.Skip(1).All(c => c >= '0' && c <= '9')) return "Can't be negative.";
        if (!t.All(c => c >= '0' && c <= '9')) return "Whole numbers only" + zeroHint + ".";
        if (t.Length > 9 || int.Parse(t) > max) return $"Too large (maximum {max:N0}).";
        if (int.Parse(t) < min) return $"Minimum is {min}.";
        return null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;   // Save is disabled while invalid; this is just a safety net

        var current = SettingsService.Current;
        var s = new AppSettings
        {
            PanelHotkey = PanelHotkeyBox.Text.Trim(),
            FallbackHotkey = FallbackHotkeyBox.Text.Trim(),
            PlainPasteHotkey = PlainHotkeyBox.Text.Trim(),
            QueueHotkey = QueueHotkeyBox.Text.Trim(),
            QueueToggleHotkey = QueueToggleHotkeyBox.Text.Trim(),
            FetchLinkTitles = FetchLinksBox.IsChecked == true,
            PinPlacement = PinBottom.IsChecked == true ? PinPlacement.Bottom : PinTab.IsChecked == true ? PinPlacement.SeparateTab : PinPlacement.Top,
            AutoPinNotes = AutoPinNotes.IsChecked == true,
            MaxTempItems = int.Parse(MaxItemsBox.Text.Trim()),
            MaxTempMemoryMb = int.Parse(MaxMemBox.Text.Trim()),
            MaxTempAgeHours = int.Parse(MaxAgeBox.Text.Trim()),
            MergeDuplicates = MergeDupes.IsChecked == true,
            ExplorerImageSave = ExplorerSave.IsChecked == true,
            StartWithWindows = StartWithWindows.IsChecked == true,
            Theme = ThemeDark.IsChecked == true ? ThemeMode.Dark : ThemeLight.IsChecked == true ? ThemeMode.Light : ThemeMode.System,
            EncryptHistory = EncryptBox.IsChecked == true,
            SkipPasswordManagerFlag = SkipFlagBox.IsChecked == true,
            SensitiveMode = SensSkip.IsChecked == true ? SensitiveMode.Skip : SensWipe.IsChecked == true ? SensitiveMode.AutoWipe : SensitiveMode.Off,
            SensitiveWipeSeconds = int.Parse(WipeSecondsBox.Text.Trim()),
            ExcludedApps = ExcludedBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
            // not edited here, but must survive the save
            PanelWidth = current.PanelWidth,
            PanelHeight = current.PanelHeight
        };

        SettingsService.Update(s);   // applies theme, hotkeys and limits via the Changed event
        Close();
    }

    private void Export_Click(object sender, RoutedEventArgs e) => OpenBackup(export: true);
    private void Import_Click(object sender, RoutedEventArgs e) => OpenBackup(export: false);

    private void OpenBackup(bool export)
    {
        var w = new BackupWindow(_history, export) { Owner = this };
        w.ShowDialog();
        // import may have changed counts shown on this page
        UsageText.Text = $"Right now: {_history.TempCount()} temporary item(s) using {FormatBytes(_history.TempBytes())}, plus {_history.PinnedCount()} pinned.";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static string FormatBytes(long bytes) =>
        bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1024.0 / 1024.0:0.#} MB";
}
