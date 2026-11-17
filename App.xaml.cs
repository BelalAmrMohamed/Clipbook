using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AdvancedClipboard.Models;
using AdvancedClipboard.Services;

namespace AdvancedClipboard;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private Repository _repo = null!;
    private HistoryManager _history = null!;
    private ClipboardMonitor _monitor = null!;
    private PasteService _paste = null!;
    private HotkeyManager _hotkeys = null!;
    private MainWindow? _panel;
    private System.Windows.Forms.NotifyIcon _tray = null!;
    private System.Windows.Forms.ToolStripMenuItem _pauseItem = null!;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\AdvancedClipboard.SingleInstance", out bool isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show("Advanced Clipboard is already running. Look for its icon in the system tray.",
                "Advanced Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            AppPaths.Log("Unhandled: " + args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppPaths.Log("Fatal: " + args.ExceptionObject);

        SettingsService.Load();
        ThemeService.Apply(SettingsService.Current.Theme);
        StartupService.Set(SettingsService.Current.StartWithWindows);

        _repo = new Repository();
        _history = new HistoryManager(_repo);
        _monitor = new ClipboardMonitor(_history);
        _monitor.Start();
        _paste = new PasteService(_monitor);
        _paste.Notify += msg => Dispatcher.BeginInvoke(new Action(() => ShowBalloon(msg)));

        _hotkeys = new HotkeyManager();
        _hotkeys.Initialize();

        BuildTray();
        RegisterHotkeys(announce: true);

        SettingsService.Changed += () =>
        {
            ThemeService.Apply(SettingsService.Current.Theme);
            RegisterHotkeys(announce: false);
        };
    }

    // ---------------- Panel ----------------

    private MainWindow Panel => _panel ??= new MainWindow(_history, _paste, _monitor, OpenSettings);

    private void TogglePanel()
    {
        var panel = Panel;
        if (panel.IsVisible && panel.IsActive) { panel.HidePanel(); return; }
        _paste.CaptureTarget();   // remember the focused window/folder BEFORE we take focus
        panel.ShowPanel();
    }

    private void PastePlainFromCurrentClipboard()
    {
        try
        {
            _paste.CaptureTarget();
            if (!System.Windows.Clipboard.ContainsText()) return;
            var text = System.Windows.Clipboard.GetText();
            var item = new ClipItem { Type = ClipType.Text, Text = text };
            _ = _paste.PasteAsync(item, plainText: true);
        }
        catch (Exception ex) { AppPaths.Log("Plain paste failed: " + ex.Message); }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_history);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // ---------------- Hotkeys ----------------

    private void RegisterHotkeys(bool announce)
    {
        var s = SettingsService.Current;
        _hotkeys.Clear();

        bool primaryOk = _hotkeys.Register(s.PanelHotkey, TogglePanel);
        bool fallbackOk = false;
        if (!string.IsNullOrWhiteSpace(s.FallbackHotkey) &&
            !string.Equals(s.FallbackHotkey, s.PanelHotkey, StringComparison.OrdinalIgnoreCase))
            fallbackOk = _hotkeys.Register(s.FallbackHotkey, TogglePanel);

        if (!string.IsNullOrWhiteSpace(s.PlainPasteHotkey))
            _hotkeys.Register(s.PlainPasteHotkey, PastePlainFromCurrentClipboard);

        if (!announce) return;

        if (primaryOk)
            ShowBalloon($"Advanced Clipboard is running. Press {s.PanelHotkey} to open.");
        else if (fallbackOk)
            ShowBalloon($"{s.PanelHotkey} is unavailable, so {s.FallbackHotkey} was used instead.");
        else
            ShowBalloon("Could not register any hotkey. Open the tray menu to use the clipboard and change hotkeys in Settings.");
    }

    // ---------------- Tray ----------------

    private void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open clipboard", null, (_, _) => Dispatcher.BeginInvoke(new Action(TogglePanel)));
        _pauseItem = new System.Windows.Forms.ToolStripMenuItem("Pause recording") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) =>
        {
            _monitor.Paused = _pauseItem.Checked;
            _tray.Text = _pauseItem.Checked ? "Advanced Clipboard (paused)" : "Advanced Clipboard";
        };
        menu.Items.Add(_pauseItem);
        menu.Items.Add("Settings…", null, (_, _) => Dispatcher.BeginInvoke(new Action(OpenSettings)));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(new Action(Shutdown)));

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Advanced Clipboard",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
                Dispatcher.BeginInvoke(new Action(TogglePanel));
        };
    }

    private void ShowBalloon(string text)
    {
        try { _tray.ShowBalloonTip(2500, "Advanced Clipboard", text, System.Windows.Forms.ToolTipIcon.None); }
        catch { /* ignore */ }
    }

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var board = new SolidBrush(Color.FromArgb(0, 120, 212));
            using var paper = new SolidBrush(Color.White);
            using var clip = new SolidBrush(Color.FromArgb(40, 40, 40));
            g.FillRectangle(board, 5, 5, 22, 25);
            g.FillRectangle(paper, 8, 10, 16, 17);
            g.FillRectangle(clip, 11, 2, 10, 7);
            using var line = new Pen(Color.FromArgb(0, 120, 212), 2);
            g.DrawLine(line, 10, 15, 22, 15);
            g.DrawLine(line, 10, 19, 22, 19);
            g.DrawLine(line, 10, 23, 18, 23);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _tray?.Dispose();
            _hotkeys?.Dispose();
            _monitor?.Dispose();
            _singleInstance?.ReleaseMutex();
        }
        catch { /* shutting down */ }
        base.OnExit(e);
    }
}
