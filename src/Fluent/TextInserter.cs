using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Fluent.Core;

namespace Fluent;

public enum InsertKind { Pasted, Typed, Copied, Failed }

public sealed record InsertOutcome(InsertKind Kind, string Detail);

/// <summary>Puts a transcript where the user is typing, in any app, and leaves it on the clipboard too so it
/// can be pasted again (Wispr Flow does the same).
///
/// Windows has no reliable "insert at caret" call for other apps, so the text goes in through the
/// clipboard and Ctrl+V (what people do by hand, and what works in Win32, WPF, UWP, WebView2, Chromium and
/// Electron apps alike). UI Automation only helps: when it can read the field, the text is spaced against
/// the words around the caret and checked afterwards, and typed as Unicode keystrokes if the paste was
/// ignored. When it cannot (Store apps such as WhatsApp report their WebView2 host as a plain pane), the
/// paste still goes to the app in front. Only password fields are refused; the text is copied instead.</summary>
public static class TextInserter
{
    public static async Task<InsertOutcome> InsertAsync(string text, FocusedField? fallback, IntPtr appWindow = default)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return new(InsertKind.Failed, "empty transcription");

        // Fluent's own window in front (the user clicked it while talking): go back to the app the words are for.
        var front = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(front, out var frontPid);
        if (front == IntPtr.Zero || frontPid == Environment.ProcessId)
        {
            var w = fallback?.Window ?? appWindow;
            if (w != IntPtr.Zero && Native.IsWindow(w))
            {
                Native.SetForegroundWindow(w);
                await Task.Delay(120);
                front = Native.GetForegroundWindow();
            }
            else return await CopyOnly(trimmed, "Fluent is the app in front");
        }
        if (fallback?.Kind == FieldKind.Secure) return await CopyOnly(trimmed, "password field", secret: true);
        // An app running as administrator ignores keys from Fluent (Windows' UIPI) without any error:
        // pasting would look done and do nothing, so say so instead.
        Native.GetWindowThreadProcessId(front, out var targetPid);
        if (!Native.SelfElevated && Native.IsElevated(targetPid) == true) return await CopyOnly(trimmed, AdminApp);

        // The field focused now wins; the one captured when dictation started is the fallback, but only
        // while its window is still in front (the paste always goes to the window in front).
        var field = await FieldFinder.FrontmostAsync(1200);
        if (field?.Kind != FieldKind.Editable && fallback is { Kind: FieldKind.Editable } && fallback.Window == front
            && (field is null || field.Pid == fallback.Pid))
            field = fallback;
        if (field?.Kind == FieldKind.Secure) return await CopyOnly(trimmed, "password field", secret: true);

        await WaitForModifiersUp();
        if (field?.Kind != FieldKind.Editable)
        {
            // Nothing UI Automation can read or check: paste at the app's own caret, as a person would.
            Log.Write($"field {(field is null ? "unknown" : field.Traits.ControlType)} in {field?.Process ?? "?"}, pasting anyway");
            return await PasteAsync(trimmed, trimmed) ? new(InsertKind.Pasted, trimmed) : await CopyOnly(trimmed, "clipboard busy");
        }

        var before = await FieldFinder.SnapshotAsync(field.Element, 1000);
        var piece = before is { Text: { } t, SelStart: >= 0 }
            ? InsertionRules.SpacedInsertion(t, before.SelStart, before.SelEnd, trimmed)
            : trimmed;
        if (piece.Length == 0) return new(InsertKind.Failed, "nothing to insert");
        string? expected = before is { Text: { } bt, SelStart: >= 0 }
            ? bt[..before.SelStart] + piece + bt[before.SelEnd..]
            : null;

        var pasted = await PasteAsync(piece, trimmed);
        if (pasted && before?.Text is null) return new(InsertKind.Pasted, piece);   // nothing to check against
        if (pasted)
        {
            var check = await CheckAsync(field, before!, expected);
            if (check != Landing.Unchanged)
            {
                // Changed but not as expected: the paste went in and the app is still updating (or reformatted
                // it). Typing now would put the words in twice.
                if (check == Landing.Changed) Log.Write($"paste changed the field in {field.Process} differently than expected");
                return new(InsertKind.Pasted, piece);
            }
        }

        // The paste was ignored (the field did not change at all) or the clipboard was busy: type it instead.
        Log.Write($"paste not confirmed in {field.Process}, typing");
        if (!Native.TypeUnicode(piece)) return await CopyOnly(trimmed, "the app ignored the text");
        if (before?.Text is null) return new(InsertKind.Typed, piece);   // nothing to check against
        if (await CheckAsync(field, before, expected) != Landing.Unchanged) return new(InsertKind.Typed, piece);
        return await CopyOnly(trimmed, "the app ignored the text");
    }

    enum Landing { Landed, Changed, Unchanged }

    /// <summary>Reads the field back a few times: landed as expected, changed some other way, or untouched.</summary>
    static async Task<Landing> CheckAsync(FocusedField field, FieldSnapshot before, string? expected)
    {
        if (before.Text is null) return Landing.Unchanged;
        string? last = null;
        foreach (var wait in new[] { 60, 150, 300, 500, 700 })
        {
            await Task.Delay(wait);
            var after = await FieldFinder.SnapshotAsync(field.Element, 1000);
            if (InsertionRules.Landed(before.Text, after?.Text, expected ?? before.Text)) return Landing.Landed;
            if (after?.Text is { } a) last = a;
        }
        return last is not null && last != before.Text ? Landing.Changed : Landing.Unchanged;
    }

    public const string AdminApp = "the app runs as administrator";

    /// <summary>Copies without inserting. For a password field the copy is kept out of clipboard history.</summary>
    static async Task<InsertOutcome> CopyOnly(string text, string why, bool secret = false)
    {
        var ok = await SetClipboardAsync(text, transient: secret);
        return new(ok ? InsertKind.Copied : InsertKind.Failed, why);
    }

    /// <summary>Ctrl+V must not mix with keys the user is still holding (the hold-to-talk key).</summary>
    static async Task WaitForModifiersUp()
    {
        for (var i = 0; i < 30 && Native.AnyModifierDown(); i++) await Task.Delay(50);
    }

    /// <summary>Puts <paramref name="piece"/> on the clipboard and presses Ctrl+V. Afterwards the clipboard
    /// holds <paramref name="keep"/> (the transcript without the spacing added for this spot), so the user
    /// can paste it again anywhere.</summary>
    static async Task<bool> PasteAsync(string piece, string keep)
    {
        var same = piece == keep;
        if (!await SetClipboardAsync(piece, transient: !same)) return false;
        if (!Native.CtrlV()) return false;
        if (!same) _ = KeepLaterAsync(piece, keep);
        return true;
    }

    static async Task KeepLaterAsync(string pasted, string keep)
    {
        // Apps read the clipboard a moment after Ctrl+V; swapping it sooner could paste the wrong text.
        await Task.Delay(600);
        try
        {
            // Only if the clipboard still holds what Fluent put there: never overwrite something the user just copied.
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    if (Clipboard.ContainsText() && Clipboard.GetText() == pasted) await SetClipboardAsync(keep, transient: false);
                    return;
                }
                catch { await Task.Delay(80); }   // another app has the clipboard open
            }
        }
        catch (Exception e) { Log.Write("keep clipboard: " + e.Message); }
    }

    /// <summary>Sets clipboard text. <paramref name="transient"/> keeps it out of Win+V history and cloud
    /// clipboard sync, since it is only there for the paste.</summary>
    public static async Task<bool> SetClipboardAsync(string text, bool transient)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.UnicodeText, text);
                if (transient)
                {
                    data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
                    data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
                    data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
                }
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch { await Task.Delay(40); }   // another app has the clipboard open
        }
        return false;
    }
}
