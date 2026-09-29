using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Fluent.Core;

namespace Fluent;

/// <summary>Hold-to-talk and the start/stop shortcut, both on any key or combination the user records in
/// Settings (Mac: HotkeyManager). One low-level keyboard hook, no admin rights. <see cref="HotkeyEngine"/>
/// decides; this class feeds it and acts. The hook only swallows the ordinary key of a combination while
/// Fluent is using it (F8, Caps Lock, the Space of Ctrl + Alt + Space); modifiers always pass through.</summary>
public sealed class HotkeyManager : IDisposable
{
    readonly AppModel model;
    readonly OverlayController overlays;
    readonly HotkeyEngine engine = new();
    readonly DispatcherTimer holdTimer = new() { Interval = HoldGesture.HoldDelay };
    readonly Native.HookProc proc;   // kept alive: the hook calls it from native code
    IntPtr hook;
    bool holdStartedDictation;
    readonly KeyRecordingSession recording = new();
    Action<KeyCombo?>? recorded;

    /// <summary>Why the shortcuts cannot work (the hook could not be installed), for Settings.</summary>
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
        Sync();
        model.ShortcutsChanged += Sync;
        hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
        {
            Problem = "Windows would not let Fluent watch the keyboard, so the shortcuts are off. Restart Fluent to try again.";
            Log.Write("keyboard hook failed: " + Marshal.GetLastWin32Error());
        }
    }

    void Sync()
    {
        engine.Hold = model.HoldKey;
        engine.Toggle = model.ToggleShortcut;
    }

    /// <summary>Settings: the next keys pressed become a combination. <paramref name="done"/> gets null when
    /// the user pressed Esc or <see cref="CancelRecording"/> was called. Keys pressed meanwhile reach no app.</summary>
    public void Record(Action<KeyCombo?> done)
    {
        CancelRecording();
        holdTimer.Stop();
        recording.Start();
        recorded = done;
    }

    public void CancelRecording()
    {
        if (!recording.Recording) return;
        Finish(null);
    }

    public bool Recording => recording.Recording;

    void Finish(KeyCombo? combo)
    {
        var done = recorded;
        recording.Stop();
        recorded = null;
        engine.Reset();
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => done?.Invoke(combo));
    }

    // Runs on the UI thread (the one that installed the hook) and must return quickly.
    IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();
            var down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            var up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            if (k.dwExtraInfo != Native.InjectedTag && (down || up))
            {
                var vk = (int)k.vkCode;
                // The user left Settings mid-recording: keys typed elsewhere must never become a shortcut.
                if (recording.Recording && !FluentInFront()) Finish(null);
                var (swallowRec, result, combo) = recording.Key(vk, down);
                if (result == KeyComboRecorder.Result.Done) Finish(combo);
                else if (result == KeyComboRecorder.Result.Cancelled) Finish(null);
                if (swallowRec) return new IntPtr(1);

                engine.Active = model.TermsAccepted;
                var (swallow, signals) = engine.Key(vk, down, Environment.TickCount64 / 1000.0, model.Dictation.IsLive);
                if (signals.Count > 0)
                {
                    // Masking must happen while the modifier is still down, so it is not deferred.
                    if (signals.Contains(HotkeyEngine.Signal.Mask)) Native.MaskModifierRelease();
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Act(signals));
                }
                if (swallow) return new IntPtr(1);
            }
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    void Act(System.Collections.Generic.List<HotkeyEngine.Signal> signals)
    {
        foreach (var s in signals)
        {
            switch (s)
            {
                case HotkeyEngine.Signal.ArmHoldTimer: holdTimer.Stop(); holdTimer.Start(); break;
                case HotkeyEngine.Signal.CancelHoldTimer: holdTimer.Stop(); break;
                case HotkeyEngine.Signal.StopHold:
                    holdTimer.Stop();
                    if (holdStartedDictation) model.Dictation.Stop();
                    holdStartedDictation = false;
                    break;
                case HotkeyEngine.Signal.Toggle: _ = ToggleAsync(); break;
                case HotkeyEngine.Signal.Cancel: model.Dictation.Cancel(); break;
            }
        }
    }

    async System.Threading.Tasks.Task ToggleAsync()
    {
        if (!model.TermsAccepted) return;
        var d = model.Dictation;
        if (d.IsLive) { d.Stop(); return; }
        var field = await FieldFinder.FrontmostAsync() ?? overlays.CurrentField;
        d.Start(DictationSource.Toggle, field);
    }

    async void HoldTimerFired()
    {
        if (!engine.HoldTimerFired() || model.Snoozed) return;
        var d = model.Dictation;
        if (d.IsLive) return;
        // Alt or Win released on their own would open the menu bar or the Start menu.
        if (model.HoldKey.NeedsMask) Native.MaskModifierRelease();
        holdStartedDictation = true;
        var field = await FieldFinder.FrontmostAsync() ?? overlays.CurrentField;
        if (!engine.Gesture.Dictating) { holdStartedDictation = false; return; }   // released while we looked
        d.Start(DictationSource.HoldKey, field);
    }

    static bool FluentInFront()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
        return pid == Environment.ProcessId;
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }
}
