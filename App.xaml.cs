using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Clipbook.Models;
using Clipbook.Services;

namespace Clipbook;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private Repository _repo = null!;
    private HistoryManager _history = null!;
    private ClipboardMonitor _monitor = null!;
    private PasteService _paste = null!;
    private PasteQueue _queue = null!;
    private HotkeyManager _hotkeys = null!;
    private MainWindow? _panel;
    private System.Windows.Forms.NotifyIcon _tray = null!;
    private System.Windows.Forms.ToolStripMenuItem _pauseItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _collectItem = null!;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\Clipbook.SingleInstance", out bool isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show("Clipbook is already running. Look for its icon in the system tray.",
                "Clipbook", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            AppPaths.Log("Unhandled: " + args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppPaths.Log("Fatal: " + args.ExceptionObject);

        AppPaths.MigrateLegacyData();
        SettingsService.Load();
        ThemeService.Apply(SettingsService.Current.Theme);
        StartupService.Set(SettingsService.Current.StartWithWindows);

        _repo = new Repository();
        _history = new HistoryManager(_repo);
        _monitor = new ClipboardMonitor(_history);
        _monitor.Start();
        _paste = new PasteService(_monitor);
        _paste.Notify += msg => Dispatcher.BeginInvoke(new Action(() => ShowBalloon(msg)));
        _queue = new PasteQueue(_history);
        _queue.Changed += UpdateTrayState;

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

    private MainWindow Panel => _panel ??= new MainWindow(_history, _paste, _monitor, _queue, OpenSettings);

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

    // ---------------- Paste queue ----------------

    private void PasteNextFromQueue()
    {
        var item = _queue.Dequeue();
        if (item == null)
        {
            ShowBalloon($"The paste queue is empty. Start collecting ({SettingsService.Current.QueueToggleHotkey}), copy a few things, then press {SettingsService.Current.QueueHotkey} to paste them one by one.");
            return;
        }
        _paste.CaptureTarget();
        _ = _paste.ActivateAsync(item);
        if (_queue.Count == 0) ShowBalloon("That was the last item in the queue.");
    }

    private void ToggleQueueCollecting()
    {
        _queue.ToggleCollecting();
        ShowBalloon(_queue.Collecting
            ? $"Collecting: everything you copy is added to the queue. Press {SettingsService.Current.QueueHotkey} to paste the next item."
            : $"Stopped collecting. {_queue.Count} item(s) in the queue.");
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
        if (!string.IsNullOrWhiteSpace(s.QueueHotkey))
            _hotkeys.Register(s.QueueHotkey, PasteNextFromQueue);
        if (!string.IsNullOrWhiteSpace(s.QueueToggleHotkey))
            _hotkeys.Register(s.QueueToggleHotkey, ToggleQueueCollecting);

        if (!announce) return;

        if (primaryOk)
            ShowBalloon($"Clipbook is running. Press {s.PanelHotkey} to open.");
        else if (fallbackOk)
            ShowBalloon($"{s.PanelHotkey} is unavailable, so {s.FallbackHotkey} was used instead.");
        else
            ShowBalloon("Could not register any hotkey. Open the tray menu to use the clipboard and change hotkeys in Settings.");
    }

    // ---------------- Tray ----------------

    private void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Clipbook", null, (_, _) => Dispatcher.BeginInvoke(new Action(TogglePanel)));
        _pauseItem = new System.Windows.Forms.ToolStripMenuItem("Pause recording") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) =>
        {
            _monitor.Paused = _pauseItem.Checked;
            UpdateTrayState();
        };
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _collectItem = new System.Windows.Forms.ToolStripMenuItem("Collect copies into paste queue");
        _collectItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => _queue.ToggleCollecting()));
        menu.Items.Add(_collectItem);
        menu.Items.Add("Paste next from queue", null, (_, _) => Dispatcher.BeginInvoke(new Action(PasteNextFromQueue)));
        menu.Items.Add("Clear paste queue", null, (_, _) => Dispatcher.BeginInvoke(new Action(() => _queue.Clear())));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => Dispatcher.BeginInvoke(new Action(OpenSettings)));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(new Action(Shutdown)));

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Clipbook",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
                Dispatcher.BeginInvoke(new Action(TogglePanel));
        };
    }

    private void UpdateTrayState()
    {
        if (_tray == null || _collectItem == null) return;
        _collectItem.Checked = _queue.Collecting;
        var text = "Clipbook";
        if (_pauseItem.Checked) text += " (paused)";
        if (_queue.Collecting) text += " · collecting";
        if (_queue.Count > 0) text += $" · queue {_queue.Count}";
        _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    private void ShowBalloon(string text)
    {
        try { _tray.ShowBalloonTip(2500, "Clipbook", text, System.Windows.Forms.ToolTipIcon.None); }
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
