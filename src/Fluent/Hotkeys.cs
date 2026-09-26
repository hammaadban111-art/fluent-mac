using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Fluent.Core;

namespace Fluent;

/// <summary>Hold-to-talk on a key and the start/stop shortcut (Mac: HotkeyManager). The shortcut uses
/// RegisterHotKey; the hold key uses a low-level keyboard hook that only watches (it never blocks or
/// changes a keystroke) and needs no admin rights.</summary>
public sealed class HotkeyManager : IDisposable
{
    readonly AppModel model;
    readonly OverlayController overlays;
    readonly HoldGesture gesture = new();
    readonly DispatcherTimer holdTimer = new() { Interval = HoldGesture.HoldDelay };
    readonly HashSet<int> held = [];
    readonly Native.HookProc proc;   // kept alive: the hook calls it from native code
    IntPtr hook;
    HwndSource? sink;
    const int ToggleId = 0x464C;
    bool holdStartedDictation;

    /// <summary>Why the start/stop shortcut could not be registered (another app owns it), for Settings.</summary>
    public string? Problem { get; private set; }

    public HotkeyManager(AppModel model, OverlayController overlays)
    {
        this.model = model;
        this.overlays = overlays;
        proc = HookCallback;
        holdTimer.Tick += (_, _) => { holdTimer.Stop(); HoldTimerFired(); };
    }

    public void Install()
    {
        sink = new HwndSource(new HwndSourceParameters("Fluent hotkeys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
        sink.AddHook(WndProc);
        RegisterToggle();
        model.ShortcutsChanged += RegisterToggle;
        hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) Log.Write("keyboard hook failed: " + Marshal.GetLastWin32Error());
    }

    public void RegisterToggle()
    {
        if (sink is null) return;
        Native.UnregisterHotKey(sink.Handle, ToggleId);
        Problem = null;
        var s = model.ToggleShortcut;
        if (s.IsOff) return;
        if (!Native.RegisterHotKey(sink.Handle, ToggleId, s.Modifiers | ToggleShortcut.NoRepeat, s.Key))
        {
            Problem = $"{s.Label} is already used by another app. Pick a different shortcut.";
            Log.Write("hotkey taken: " + s.Label);
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && w.ToInt32() == ToggleId)
        {
            handled = true;
            _ = ToggleAsync();
        }
        return IntPtr.Zero;
    }

    async System.Threading.Tasks.Task ToggleAsync()
    {
        if (!model.TermsAccepted) return;
        var d = model.Dictation;
        if (d.IsLive) { d.Stop(); return; }
        var field = await FieldFinder.FrontmostAsync() ?? overlays.CurrentField;
        d.Start(DictationSource.Toggle, field);
    }

    IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();
            if (k.dwExtraInfo != Native.InjectedTag)
            {
                var down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                var up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
                var vk = (int)k.vkCode;
                // Handle on the UI thread; the hook must return at once.
                if (down || up) System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Handle(vk, down));
            }
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    void Handle(int vk, bool down)
    {
        var d = model.Dictation;
        if (down && vk == HoldKeyInfo.VkEscape && d.IsLive) { d.Cancel(); return; }
        var key = model.HoldKey;
        if (down) held.Add(vk); else held.Remove(vk);
        if (key == HoldKey.Off || !model.TermsAccepted) return;
        if (key.IsPart(vk))
        {
            var isDown = key.IsDown(held);
            if (isDown && gesture.PressedAt is null && down) Act(gesture.Down(Environment.TickCount64 / 1000.0));
            else if (!isDown && gesture.PressedAt is not null) Act(gesture.Up());
        }
        else if (down && gesture.PressedAt is not null)
        {
            Act(gesture.OtherKey());   // Ctrl+C is a shortcut, not a hold
        }
    }

    void Act(HoldGesture.Action a)
    {
        switch (a)
        {
            case HoldGesture.Action.ArmTimer: holdTimer.Stop(); holdTimer.Start(); break;
            case HoldGesture.Action.CancelTimer: holdTimer.Stop(); break;
            case HoldGesture.Action.StopAndInsert:
                holdTimer.Stop();
                if (holdStartedDictation) model.Dictation.Stop();
                holdStartedDictation = false;
                break;
        }
    }

    async void HoldTimerFired()
    {
        if (!gesture.TimerFired() || model.Snoozed) return;
        var d = model.Dictation;
        if (d.IsLive) return;
        // Alt or Win released on their own would open the menu bar or the Start menu.
        if (model.HoldKey is HoldKey.RightAlt or HoldKey.CtrlWin) Native.MaskModifierRelease();
        holdStartedDictation = true;
        var field = await FieldFinder.FrontmostAsync() ?? overlays.CurrentField;
        if (!gesture.Dictating) { holdStartedDictation = false; return; }   // released while we looked
        d.Start(DictationSource.HoldKey, field);
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
        if (sink is not null) { Native.UnregisterHotKey(sink.Handle, ToggleId); sink.Dispose(); }
    }
}
