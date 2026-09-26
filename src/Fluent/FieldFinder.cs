using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Fluent.Core;

namespace Fluent;

/// <summary>The text box that has the keyboard focus, as UI Automation reports it. Rectangles are screen
/// pixels (physical, top-left origin).</summary>
public sealed record FocusedField(
    AutomationElement Element, FieldKind Kind, FieldTraits Traits, BubblePlacement.Rect Frame,
    IntPtr Window, int Pid, string Process, string Title)
{
    public string AppName => FieldFinder.FriendlyName(Pid, Process);
    public StyleCategory Category => AppCategories.Category(Process, Title);
}

/// <summary>What the field holds right now, for spacing and for checking that an insert landed.</summary>
public sealed record FieldSnapshot(string? Text, int SelStart, int SelEnd);

/// <summary>All UI Automation work runs on one background MTA thread with short timeouts. UI Automation
/// calls into other apps; doing that on the UI thread would freeze Fluent whenever an app hangs, and
/// could deadlock when the app being asked is Fluent itself.</summary>
public static class FieldFinder
{
    static readonly BlockingCollection<Action> Work = new();
    static UIA3Automation? automation;
    static readonly int OwnPid = Environment.ProcessId;
    static readonly ConcurrentDictionary<int, (string Name, string Friendly)> Processes = new();

    static FieldFinder()
    {
        var t = new Thread(() =>
        {
            automation = new UIA3Automation();
            try
            {
                automation.ConnectionTimeout = TimeSpan.FromMilliseconds(1500);
                automation.TransactionTimeout = TimeSpan.FromMilliseconds(1500);
            }
            catch { /* older Windows without IUIAutomation2 */ }
            foreach (var job in Work.GetConsumingEnumerable())
            {
                try { job(); } catch (Exception e) { Log.Write("uia: " + e.Message); }
            }
        }) { IsBackground = true, Name = "Fluent UI Automation" };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
    }

    /// <summary>Runs <paramref name="f"/> on the automation thread; gives up (default) after the timeout.</summary>
    public static Task<T?> Run<T>(Func<UIA3Automation, T?> f, int timeoutMs = 2500)
    {
        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Add(() =>
        {
            try { tcs.TrySetResult(automation is null ? default : f(automation)); }
            catch (Exception e) { Log.Write("uia job: " + e.Message); tcs.TrySetResult(default); }
        });
        return tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : default);
    }

    /// <summary>The focused element of the foreground app, or null (Fluent's own windows count as none).</summary>
    public static Task<FocusedField?> FrontmostAsync() => Run(a => Frontmost(a));

    static FocusedField? Frontmost(UIA3Automation a)
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == OwnPid) return null;
        var el = a.FocusedElement();
        if (el is null) return null;
        var traits = Traits(el);
        var kind = FieldClassifier.Classify(traits);
        var r = el.Properties.BoundingRectangle.ValueOrDefault;
        var elPid = el.Properties.ProcessId.ValueOrDefault;
        if (elPid == OwnPid) return null;
        var (name, _) = ProcessInfo((int)pid);
        return new FocusedField(el, kind, traits, new BubblePlacement.Rect(r.X, r.Y, r.Width, r.Height),
            hwnd, (int)pid, name, Native.WindowTitle(hwnd));
    }

    public static FieldTraits Traits(AutomationElement el)
    {
        var p = el.Properties;
        var value = el.Patterns.Value.PatternOrDefault;
        return new FieldTraits(
            ControlType: p.ControlType.ValueOrDefault.ToString(),
            IsPassword: p.IsPassword.ValueOrDefault,
            HasValuePattern: value is not null,
            ValueReadOnly: value?.IsReadOnly.ValueOrDefault ?? true,
            HasTextPattern: el.Patterns.Text.IsSupported,
            IsEnabled: p.IsEnabled.ValueOrDefault,
            KeyboardFocusable: p.IsKeyboardFocusable.ValueOrDefault);
    }

    /// <summary>The field's text and selection in UTF-16 offsets. Text is null when the app does not say.</summary>
    public static Task<FieldSnapshot?> SnapshotAsync(AutomationElement el) => Run(_ => Snapshot(el), 2000);

    static FieldSnapshot Snapshot(AutomationElement el)
    {
        string? text = null;
        int start = -1, end = -1;
        var value = el.Patterns.Value.PatternOrDefault;
        try { text = value?.Value.ValueOrDefault; } catch { }
        var tp = el.Patterns.Text.PatternOrDefault;
        if (tp is not null)
        {
            try
            {
                var doc = tp.DocumentRange;
                text ??= doc.GetText(-1);
                var sel = tp.GetSelection().FirstOrDefault();
                if (sel is not null)
                {
                    var before = doc.Clone();
                    before.MoveEndpointByRange(TextPatternRangeEndpoint.End, sel, TextPatternRangeEndpoint.Start);
                    start = before.GetText(-1).Length;
                    end = start + sel.GetText(-1).Length;
                }
            }
            catch { }
        }
        if (text is not null && start > text.Length) { start = end = -1; }
        return new FieldSnapshot(text, start, Math.Min(end, text?.Length ?? end));
    }

    public static (string Name, string Friendly) ProcessInfo(int pid) => Processes.GetOrAdd(pid, id =>
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(id);
            var name = p.ProcessName;
            string friendly = name;
            try { friendly = p.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } d ? d : name; } catch { }
            return (name, friendly);
        }
        catch { return ("", ""); }
    });

    public static string FriendlyName(int pid, string process) =>
        ProcessInfo(pid).Friendly is { Length: > 0 } f ? f : process;

    /// <summary>Apps with windows, for Settings → "Hidden in".</summary>
    public static List<(string Process, string Name)> RunningApps() =>
        System.Diagnostics.Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.Id != OwnPid; } catch { return false; } })
            .Select(p => (AppCategories.Normalize(p.ProcessName), FriendlyName(p.Id, p.ProcessName)))
            .GroupBy(x => x.Item1).Select(g => g.First())
            .OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>A small rolling log in %LOCALAPPDATA%\Fluent\fluent.log (text only; never transcripts or keys).</summary>
public static class Log
{
    static readonly object Gate = new();
    public static string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fluent", "fluent.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var fi = new System.IO.FileInfo(Path);
                if (fi.Exists && fi.Length > 512 * 1024) System.IO.File.Move(Path, Path + ".1", true);
                System.IO.File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            Debug.WriteLine(line);
        }
        catch { }
    }
}
