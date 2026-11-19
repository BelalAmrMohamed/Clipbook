using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Clipbook.Services;

namespace Clipbook.Native;

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

    // FindWindowEx returns child windows in Z-order, topmost first. Explorer keeps the ACTIVE tab's
    // ShellTabWindowClass window on top, so the first one found is the tab the user is looking at.
    // (IsWindowVisible is useless here: every tab window reports visible on Windows 11.)
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowTitle);

    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid SidShellBrowser = new("000214E2-0000-0000-C000-000000000046");
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

            // Tab window (ShellTabWindowClass) of the active tab in this Explorer frame; Zero on Windows 10 / no tabs.
            var activeTab = FindWindowEx(hwnd, IntPtr.Zero, "ShellTabWindowClass", null);

            var candidates = new List<(string path, IntPtr tab)>();
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

                    candidates.Add((path, GetTabWindow(w)));
                }
                catch { /* some shell windows are not folder views */ }
            }

            // One folder view: that's it. Several tabs: pick the one whose tab window is the active one.
            // If it can't be determined, return null rather than guess: Clipbook then pastes normally
            // instead of saving into a folder the user isn't looking at.
            string? chosen = null;
            if (candidates.Count == 1) chosen = candidates[0].path;
            else if (candidates.Count > 1)
            {
                var active = activeTab == IntPtr.Zero
                    ? new List<(string path, IntPtr tab)>()
                    : candidates.FindAll(c => c.tab == activeTab);
                if (active.Count == 1) chosen = active[0].path;
                else AppPaths.Log($"Explorer: could not tell the active tab apart (activeTab=0x{activeTab.ToInt64():X}, " +
                                  $"tabs=[{string.Join(", ", candidates.ConvertAll(c => "0x" + c.tab.ToInt64().ToString("X")))}]).");
            }
            return chosen != null && Directory.Exists(chosen) ? chosen : null;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Explorer folder lookup failed: " + ex.Message);
            return null;
        }
    }

    // Explorer on Windows 11 hosts several tabs in one top-level window; each tab has its own
    // ShellTabWindowClass window. Returns this folder view's tab window, or Zero if unknown.
    private static IntPtr GetTabWindow(object shellWindow)
    {
        try
        {
            var sp = (IServiceProviderCom)shellWindow;
            // Try the per-frame service first (what Explorer-tab scripts use), then the per-view one.
            foreach (var service in new[] { SidTopLevelBrowser, SidShellBrowser })
            {
                var sid = service;
                var iid = IidShellBrowser;
                if (sp.QueryService(ref sid, ref iid, out var browserObj) != 0 || browserObj == null) continue;
                var browser = (IShellBrowserCom)browserObj;
                if (browser.GetWindow(out var tabHwnd) == 0 && tabHwnd != IntPtr.Zero) return tabHwnd;
            }
        }
        catch { }
        return IntPtr.Zero;
    }
}
