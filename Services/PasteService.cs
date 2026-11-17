using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AdvancedClipboard.Models;
using AdvancedClipboard.Native;

namespace AdvancedClipboard.Services;

public sealed class PasteService
{
    private readonly ClipboardMonitor _monitor;
    private IntPtr _targetWindow = IntPtr.Zero;

    /// <summary>Folder shown by the Explorer window that was focused when the panel opened (or null).</summary>
    public string? TargetFolder { get; private set; }

    public event Action<string>? Notify;

    public PasteService(ClipboardMonitor monitor) => _monitor = monitor;

    /// <summary>Call BEFORE showing the panel: remembers where the user was working.</summary>
    public void CaptureTarget()
    {
        _targetWindow = NativeMethods.GetForegroundWindow();
        TargetFolder = null;
        if (_targetWindow != IntPtr.Zero && SettingsService.Current.ExplorerImageSave && ExplorerService.IsExplorerWindow(_targetWindow))
            TargetFolder = ExplorerService.GetFolderPath(_targetWindow);
    }

    // ---------------- Main entry points ----------------

    /// <summary>Activates an item: images go to the Explorer folder as a file when one is open, everything else is pasted.</summary>
    public async Task ActivateAsync(ClipItem item, bool plainText = false)
    {
        if (item.IsImage && TargetFolder != null)
        {
            var saved = SaveImageToFolder(item, TargetFolder);
            if (saved != null)
            {
                Notify?.Invoke($"Saved {Path.GetFileName(saved)} to {TargetFolder}");
                return;
            }
        }
        await PasteAsync(item, plainText);
    }

    public async Task PasteAsync(ClipItem item, bool plainText = false)
    {
        if (!SetClipboard(item, plainText)) { Notify?.Invoke("Could not access the clipboard."); return; }
        await RestoreFocusAndSendPasteAsync();
    }

    /// <summary>Copies only (no paste keystroke).</summary>
    public bool CopyOnly(ClipItem item, bool plainText = false) => SetClipboard(item, plainText);

    // ---------------- Image -> file ----------------

    public string? SaveImageToFolder(ClipItem item, string folder)
    {
        try
        {
            var png = ImageStore.LoadBytes(item.ImageFile);
            if (png == null || !Directory.Exists(folder)) return null;

            var baseName = $"Clipboard_{DateTime.Now:yyyy-MM-dd_HHmmss}";
            var path = Path.Combine(folder, baseName + ".png");
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(folder, $"{baseName}_{n}.png");

            File.WriteAllBytes(path, png);
            return path;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Image save to folder failed: " + ex.Message);
            Notify?.Invoke("Could not save the image there: " + ex.Message);
            return null;
        }
    }

    // ---------------- Clipboard write ----------------

    private bool SetClipboard(ClipItem item, bool plainText)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                _monitor.SuppressNext();
                switch (item.Type)
                {
                    case ClipType.Image:
                        var bmp = ImageStore.LoadFull(item.ImageFile);
                        if (bmp == null) return false;
                        System.Windows.Clipboard.SetImage(bmp);
                        return true;

                    case ClipType.Files:
                        var paths = (item.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                                     .Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
                        if (paths.Length == 0)
                        {
                            System.Windows.Clipboard.SetText(item.Text ?? "");
                            return true;
                        }
                        var sc = new StringCollection();
                        sc.AddRange(paths);
                        System.Windows.Clipboard.SetFileDropList(sc);
                        return true;

                    default:
                        var data = new System.Windows.DataObject();
                        data.SetText(item.Text ?? "", System.Windows.TextDataFormat.UnicodeText);
                        if (!plainText && !string.IsNullOrEmpty(item.Html))
                            data.SetText(item.Html, System.Windows.TextDataFormat.Html);
                        System.Windows.Clipboard.SetDataObject(data, true);
                        return true;
                }
            }
            catch (COMException) { Thread.Sleep(50); }
            catch (Exception ex) { AppPaths.Log("SetClipboard failed: " + ex.Message); return false; }
        }
        return false;
    }

    // ---------------- Focus + Ctrl+V ----------------

    private async Task RestoreFocusAndSendPasteAsync()
    {
        if (_targetWindow != IntPtr.Zero && NativeMethods.IsWindow(_targetWindow))
        {
            ForceForeground(_targetWindow);
            await Task.Delay(120);
        }
        else
        {
            await Task.Delay(80);
        }
        ReleaseModifiers();
        SendCtrlV();
    }

    private static void ForceForeground(IntPtr hwnd)
    {
        // Attaching to the foreground thread's input queue lets SetForegroundWindow succeed reliably.
        var fg = NativeMethods.GetForegroundWindow();
        uint fgThread = NativeMethods.GetWindowThreadProcessId(fg, out _);
        uint me = NativeMethods.GetCurrentThreadId();
        bool attached = fgThread != me && NativeMethods.AttachThreadInput(me, fgThread, true);
        NativeMethods.SetForegroundWindow(hwnd);
        if (attached) NativeMethods.AttachThreadInput(me, fgThread, false);
    }

    private static void ReleaseModifiers()
    {
        // If the user is still physically holding Win/Alt/Shift, make Windows think they let go so Ctrl+V is clean.
        var keys = new[] { NativeMethods.VK_LWIN, NativeMethods.VK_RWIN, NativeMethods.VK_MENU, NativeMethods.VK_SHIFT };
        var list = keys.Where(k => (NativeMethods.GetAsyncKeyState(k) & 0x8000) != 0).ToArray();
        if (list.Length == 0) return;
        var inputs = list.Select(k => Key((ushort)k, true)).ToArray();
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            Key(NativeMethods.VK_CONTROL, false), Key(NativeMethods.VK_V, false),
            Key(NativeMethods.VK_V, true), Key(NativeMethods.VK_CONTROL, true)
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT Key(ushort vk, bool up) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0u,
                dwExtraInfo = NativeMethods.InjectedMarker
            }
        }
    };
}
