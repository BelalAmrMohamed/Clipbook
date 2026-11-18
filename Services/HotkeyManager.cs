using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Clipbook.Native;

namespace Clipbook.Services;

public readonly record struct Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, int Vk)
{
    public bool IsValid => Vk != 0;

    public static Hotkey Parse(string? text) =>
        TryParseStrict(text, out var hotkey, out _) ? hotkey : default;

    /// <summary>Strict parser used by Settings for live validation (also used at runtime).</summary>
    public static bool TryParseStrict(string? text, out Hotkey hotkey, out string error)
    {
        hotkey = default;
        error = "";
        if (string.IsNullOrWhiteSpace(text)) { error = "Enter a hotkey, for example Ctrl+Shift+V."; return false; }

        bool ctrl = false, alt = false, shift = false, win = false;
        int vk = 0;
        string? keyName = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win": case "windows": case "meta": win = true; break;
                default:
                    if (keyName != null)
                    {
                        error = $"Only one main key is allowed (found \u201c{keyName}\u201d and \u201c{raw}\u201d).";
                        return false;
                    }
                    if (!Enum.TryParse<Key>(raw, true, out var key) || !Enum.IsDefined(typeof(Key), key) || key == Key.None)
                    {
                        error = $"\u201c{raw}\u201d is not a key name I recognise. Use letters, digits, F1\u2013F12, etc.";
                        return false;
                    }
                    keyName = raw;
                    vk = KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }

        if (vk == 0) { error = "Add a main key, for example Win+V."; return false; }
        if (!(ctrl || alt || shift || win)) { error = "Add a modifier: Ctrl, Alt, Shift or Win."; return false; }

        hotkey = new Hotkey(ctrl, alt, shift, win, vk);
        return true;
    }

    public uint Modifiers =>
        (Ctrl ? NativeMethods.MOD_CONTROL : 0) | (Alt ? NativeMethods.MOD_ALT : 0) |
        (Shift ? NativeMethods.MOD_SHIFT : 0) | (Win ? NativeMethods.MOD_WIN : 0);
}

/// <summary>
/// Registers global hotkeys. Hotkeys that include the Windows key (such as Win+V, which Windows
/// reserves) are handled by a low-level keyboard hook that swallows the keystroke; everything else
/// uses RegisterHotKey. Each action can have a primary and a fallback hotkey.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private sealed class Binding { public Hotkey Key; public Action Action = null!; public int Id; public bool ViaHook; }

    private readonly List<Binding> _bindings = new();
    private HwndSource? _source;
    private NativeMethods.LowLevelKeyboardProc? _proc;   // keep delegate alive
    private IntPtr _hook = IntPtr.Zero;
    private int _nextId = 0xB000;

    public bool HookActive => _hook != IntPtr.Zero;

    public void Initialize()
    {
        _source = new HwndSource(new HwndSourceParameters("ClipbookHotkeys") { ParentWindow = new IntPtr(-3) });
        _source.AddHook(WndProc);
    }

    public void Clear()
    {
        foreach (var b in _bindings.Where(b => !b.ViaHook))
            NativeMethods.UnregisterHotKey(_source!.Handle, b.Id);
        _bindings.Clear();
        RemoveHook();
    }

    /// <summary>Returns true if the hotkey is active (hook installed or RegisterHotKey succeeded).</summary>
    public bool Register(string hotkeyText, Action action)
    {
        var key = Hotkey.Parse(hotkeyText);
        if (!key.IsValid || _source == null) return false;

        if (key.Win)
        {
            if (!EnsureHook()) return false;
            _bindings.Add(new Binding { Key = key, Action = action, ViaHook = true });
            return true;
        }

        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, key.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)key.Vk))
        {
            AppPaths.Log($"RegisterHotKey failed for '{hotkeyText}': {Marshal.GetLastWin32Error()}");
            return false;
        }
        _bindings.Add(new Binding { Key = key, Action = action, Id = id });
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            var b = _bindings.FirstOrDefault(x => !x.ViaHook && x.Id == id);
            if (b != null) { handled = true; b.Action(); }
        }
        return IntPtr.Zero;
    }

    // ------------- Low-level keyboard hook (Win+key) -------------

    private bool EnsureHook()
    {
        if (_hook != IntPtr.Zero) return true;
        _proc = HookProc;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            AppPaths.Log("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
        return _hook != IntPtr.Zero;
    }

    private void RemoveHook()
    {
        if (_hook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    private static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            if (info.dwExtraInfo != NativeMethods.InjectedMarker && (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN))
            {
                bool win = Down(NativeMethods.VK_LWIN) || Down(NativeMethods.VK_RWIN);
                if (win)
                {
                    bool ctrl = Down(NativeMethods.VK_CONTROL), alt = Down(NativeMethods.VK_MENU), shift = Down(NativeMethods.VK_SHIFT);
                    foreach (var b in _bindings)
                    {
                        if (!b.ViaHook) continue;
                        if (b.Key.Vk == (int)info.vkCode && b.Key.Ctrl == ctrl && b.Key.Alt == alt && b.Key.Shift == shift)
                        {
                            SuppressStartMenu();
                            var action = b.Action;
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(action);
                            return (IntPtr)1;   // swallow: Windows' own Win+V never sees it
                        }
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Because we swallow the key, Windows would see "Win down, Win up" and open the Start menu.
    /// Injecting an unassigned key press in between cancels that.
    /// </summary>
    private static void SuppressStartMenu()
    {
        var inputs = new NativeMethods.INPUT[2];
        for (int i = 0; i < 2; i++)
        {
            inputs[i].type = NativeMethods.INPUT_KEYBOARD;
            inputs[i].U.ki = new NativeMethods.KEYBDINPUT
            {
                wVk = 0xE8,   // unassigned virtual key
                dwFlags = i == 0 ? 0u : NativeMethods.KEYEVENTF_KEYUP,
                dwExtraInfo = NativeMethods.InjectedMarker
            };
        }
        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose()
    {
        Clear();
        _source?.Dispose();
        _source = null;
    }
}
