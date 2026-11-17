using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AdvancedClipboard.Models;
using AdvancedClipboard.Native;

namespace AdvancedClipboard.Services;

/// <summary>
/// Listens for WM_CLIPBOARDUPDATE on a hidden message-only window and turns each change
/// into a <see cref="ClipItem"/> handed to the <see cref="HistoryManager"/>.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private readonly HistoryManager _history;
    private HwndSource? _source;
    private int _suppressUntilTick;

    public ClipboardMonitor(HistoryManager history) => _history = history;

    /// <summary>When true, new clipboard contents are not recorded (tray menu: "Pause recording").</summary>
    public bool Paused { get; set; }

    public void Start()
    {
        var p = new HwndSourceParameters("AdvancedClipboardListener") { ParentWindow = new IntPtr(-3) }; // HWND_MESSAGE
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        if (!NativeMethods.AddClipboardFormatListener(_source.Handle))
            AppPaths.Log("AddClipboardFormatListener failed: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    /// <summary>Call right before the app writes to the clipboard itself, so it is not re-captured.</summary>
    public void SuppressNext(int milliseconds = 600) => _suppressUntilTick = Environment.TickCount + milliseconds;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            if (Paused || unchecked(Environment.TickCount - _suppressUntilTick) < 0) return IntPtr.Zero;
            var sourceApp = GetClipboardOwnerName();
            // Clipboard can be locked by the writer for a moment, so read slightly later.
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => Capture(sourceApp)),
                System.Windows.Threading.DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    private static string? GetClipboardOwnerName()
    {
        try
        {
            var owner = NativeMethods.GetClipboardOwner();
            if (owner == IntPtr.Zero) return null;
            NativeMethods.GetWindowThreadProcessId(owner, out var pid);
            if (pid == 0) return null;
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch { return null; }
    }

    private void Capture(string? sourceApp)
    {
        var s = SettingsService.Current;

        if (!string.IsNullOrEmpty(sourceApp) && s.ExcludedApps.Any(a => string.Equals(a, sourceApp, StringComparison.OrdinalIgnoreCase)))
            return;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                var item = Read(sourceApp, s);
                if (item != null) _history.AddCaptured(item);
                return;
            }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(40); }
            catch (Exception ex) { AppPaths.Log("Capture failed: " + ex.Message); return; }
        }
    }

    private static ClipItem? Read(string? sourceApp, AppSettings s)
    {
        // Honour the standard "please don't record" flags set by password managers.
        if (s.SkipPasswordManagerFlag && ShouldExclude()) return null;

        if (System.Windows.Clipboard.ContainsFileDropList())
        {
            var files = System.Windows.Clipboard.GetFileDropList().Cast<string>().ToList();
            if (files.Count == 0) return null;
            var text = string.Join(Environment.NewLine, files);
            var bytes = Encoding.UTF8.GetBytes(text);
            return new ClipItem
            {
                Type = ClipType.Files, Text = text, SourceApp = sourceApp,
                Hash = "files:" + Crypto.Hash(bytes), SizeBytes = bytes.Length, CreatedUtc = DateTime.UtcNow
            };
        }

        if (System.Windows.Clipboard.ContainsImage())
        {
            var bmp = System.Windows.Clipboard.GetImage();
            if (bmp == null) return null;
            var png = ImageStore.EncodePng(bmp);
            var hash = "img:" + Crypto.Hash(png);
            var file = ImageStore.Save(png);
            return new ClipItem
            {
                Type = ClipType.Image, ImageFile = file, SourceApp = sourceApp,
                Hash = hash, SizeBytes = png.Length, CreatedUtc = DateTime.UtcNow
            };
        }

        if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText))
        {
            var text = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
            if (string.IsNullOrEmpty(text)) return null;

            string? html = null;
            try { if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.Html)) html = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Html); }
            catch { /* HTML is optional */ }

            var bytes = Encoding.UTF8.GetBytes(text);
            return new ClipItem
            {
                Type = LooksLikeLink(text) ? ClipType.Link : ClipType.Text,
                Text = text, Html = html, SourceApp = sourceApp,
                Hash = "txt:" + Crypto.Hash(bytes),
                SizeBytes = bytes.Length + (html == null ? 0 : Encoding.UTF8.GetByteCount(html)),
                CreatedUtc = DateTime.UtcNow
            };
        }
        return null;
    }

    private static bool ShouldExclude()
    {
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            if (data == null) return false;
            foreach (var f in new[] { "ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
            {
                if (!data.GetDataPresent(f)) continue;
                if (f == "ExcludeClipboardContentFromMonitorProcessing") return true;
                // For the "Can..." formats a DWORD of 0 means "do not include".
                var v = data.GetData(f);
                if (v is MemoryStream ms && ms.Length >= 4 && BitConverter.ToInt32(ms.ToArray(), 0) == 0) return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    private static bool LooksLikeLink(string text)
    {
        text = text.Trim();
        if (text.Length > 2048 || text.Contains(' ') || text.Contains('\n')) return false;
        return Uri.TryCreate(text, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeFtp);
    }

    public void Dispose()
    {
        if (_source != null)
        {
            NativeMethods.RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
            _source = null;
        }
    }
}
