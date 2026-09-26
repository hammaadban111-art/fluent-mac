using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Fluent.Core;

namespace Fluent;

public enum InsertKind { Pasted, Typed, Failed }

public sealed record InsertOutcome(InsertKind Kind, string Detail);

/// <summary>Puts a transcript at the caret of the focused text box, the way Android's
/// <c>insertAtCursor</c> and the Mac's <c>TextInserter</c> do: space it against the text around the
/// caret, put it in, read the field back to confirm, and fall back when the app ignored it.
///
/// Windows has no reliable "insert at caret" call for other apps, so the text goes in through the
/// clipboard and Ctrl+V (what people do by hand, and what works in Win32, WPF, UWP, Chromium and
/// Electron apps alike). The user's clipboard is put back afterwards and the transcript is kept out of
/// Windows clipboard history. If the clipboard is busy, the text is typed as Unicode keystrokes.</summary>
public static class TextInserter
{
    public static async Task<InsertOutcome> InsertAsync(string text, FocusedField? fallback)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return new(InsertKind.Failed, "empty transcription");

        // The field focused now wins; the one captured when dictation started is the fallback.
        var field = await FieldFinder.FrontmostAsync();
        if (field?.Kind != FieldKind.Editable && fallback is { Kind: FieldKind.Editable }
            && (field is null || field.Pid == fallback.Pid))
            field = fallback;
        if (field is null) return new(InsertKind.Failed, "no focused field");
        if (field.Kind == FieldKind.Secure) return new(InsertKind.Failed, "refusing to type into a password field");
        if (field.Kind != FieldKind.Editable) return new(InsertKind.Failed, "focused field is not editable");

        var before = await FieldFinder.SnapshotAsync(field.Element);
        var piece = before is { Text: { } t, SelStart: >= 0 }
            ? InsertionRules.SpacedInsertion(t, before.SelStart, before.SelEnd, trimmed)
            : trimmed;
        if (piece.Length == 0) return new(InsertKind.Failed, "nothing to insert");
        string? expected = before is { Text: { } bt, SelStart: >= 0 }
            ? bt[..before.SelStart] + piece + bt[before.SelEnd..]
            : null;

        await WaitForModifiersUp();
        var pasted = await PasteAsync(piece);
        if (pasted && await LandedAsync(field, before, expected)) return new(InsertKind.Pasted, piece);
        if (pasted && before?.Text is null) return new(InsertKind.Pasted, piece);   // nothing to check against

        // The paste was ignored or the clipboard was busy: type it instead.
        Log.Write($"paste not confirmed in {field.Process}, typing");
        if (Native.TypeUnicode(piece) && (before?.Text is null || await LandedAsync(field, before, expected)))
            return new(InsertKind.Typed, piece);
        return new(InsertKind.Failed, "the app ignored the text");
    }

    static async Task<bool> LandedAsync(FocusedField field, FieldSnapshot? before, string? expected)
    {
        if (before?.Text is null) return false;
        // Browsers and Electron apps update their accessibility tree a moment after the edit.
        foreach (var wait in new[] { 60, 150, 300, 500 })
        {
            await Task.Delay(wait);
            var after = await FieldFinder.SnapshotAsync(field.Element);
            if (InsertionRules.Landed(before.Text, after?.Text, expected ?? before.Text)) return true;
        }
        return false;
    }

    /// <summary>Ctrl+V must not mix with keys the user is still holding (the hold-to-talk key).</summary>
    static async Task WaitForModifiersUp()
    {
        for (var i = 0; i < 30 && Native.AnyModifierDown(); i++) await Task.Delay(50);
    }

    static readonly string[] KeptFormats =
    [
        DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html,
        DataFormats.FileDrop, DataFormats.Bitmap, DataFormats.CommaSeparatedValue,
    ];

    /// <summary>Puts <paramref name="text"/> on the clipboard, presses Ctrl+V, and restores what was there.</summary>
    static async Task<bool> PasteAsync(string text)
    {
        var saved = SaveClipboard();
        if (!await SetClipboardAsync(text, transient: true)) return false;
        if (!Native.CtrlV()) return false;
        _ = RestoreLaterAsync(saved, text);
        return true;
    }

    static async Task RestoreLaterAsync(DataObject? saved, string ours)
    {
        await Task.Delay(700);
        try
        {
            // Only if the clipboard still holds the transcript: never overwrite something the user just copied.
            if (Clipboard.ContainsText() && Clipboard.GetText() == ours)
            {
                if (saved is null) Clipboard.Clear(); else Clipboard.SetDataObject(saved, true);
            }
        }
        catch (Exception e) { Log.Write("restore clipboard: " + e.Message); }
    }

    static DataObject? SaveClipboard()
    {
        try
        {
            var current = Clipboard.GetDataObject();
            if (current is null) return null;
            var copy = new DataObject();
            var any = false;
            foreach (var f in KeptFormats)
            {
                try
                {
                    if (!current.GetDataPresent(f, false)) continue;
                    var d = current.GetData(f, false);
                    if (d is null) continue;
                    copy.SetData(f, d);
                    any = true;
                }
                catch { }
            }
            return any ? copy : null;
        }
        catch { return null; }
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
