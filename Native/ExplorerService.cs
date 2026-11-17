using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AdvancedClipboard.Services;

namespace AdvancedClipboard.Native;

/// <summary>Finds which folder the foreground File Explorer window (or the Desktop) is showing.</summary>
internal static class ExplorerService
{
    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProviderCom
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    // Only GetWindow (the first IOleWindow method) is called, so the rest of the vtable is omitted.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowserCom
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
    }

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    public static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsExplorerWindow(IntPtr hwnd)
    {
        var cls = ClassOf(hwnd);
        return cls is "CabinetWClass" or "Progman" or "WorkerW" or "ExploreWClass";
    }

    /// <summary>Returns the real file-system folder shown by the given window, or null.</summary>
    public static string? GetFolderPath(IntPtr hwnd)
    {
        try
        {
            var cls = ClassOf(hwnd);
            if (cls is "Progman" or "WorkerW")
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (cls is not ("CabinetWClass" or "ExploreWClass")) return null;

            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic windows = shell.Windows();

            var candidates = new List<(string path, bool visibleTab)>();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                object? w = null;
                try { w = windows.Item(i); } catch { }
                if (w == null) continue;
                try
                {
                    dynamic win = w;
                    if ((long)win.HWND != hwnd.ToInt64()) continue;

                    string? path = null;
                    try { path = (string)win.Document.Folder.Self.Path; } catch { }
                    if (string.IsNullOrEmpty(path)) continue;

                    candidates.Add((path, IsActiveTab(w)));
                }
                catch { /* some shell windows are not folder views */ }
            }

            var active = candidates.Find(c => c.visibleTab);
            var chosen = active.path ?? (candidates.Count > 0 ? candidates[0].path : null);
            return chosen != null && Directory.Exists(chosen) ? chosen : null;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Explorer folder lookup failed: " + ex.Message);
            return null;
        }
    }

    // Explorer on Windows 11 can host several tabs in one top-level window; the active tab's
    // ShellTabWindowClass child is the visible one.
    private static bool IsActiveTab(object shellWindow)
    {
        try
        {
            var sp = (IServiceProviderCom)shellWindow;
            var sid = SidTopLevelBrowser;
            var iid = IidShellBrowser;
            if (sp.QueryService(ref sid, ref iid, out var browserObj) != 0 || browserObj == null) return true;
            var browser = (IShellBrowserCom)browserObj;
            if (browser.GetWindow(out var tabHwnd) != 0) return true;
            return IsWindowVisible(tabHwnd);
        }
        catch { return true; }
    }
}
