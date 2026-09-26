using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fluent.Core;
using static Fluent.Ui;

namespace Fluent.Views;

/// <summary>The home screen (Mac: DictateView): a big orb to try dictation right here, the last
/// transcript, quick switches and today's totals.</summary>
public sealed class DictatePage : ContentControl
{
    readonly AppModel model;
    DictationController D => model.Dictation;
    readonly Orb orb = new() { Width = 170, Height = 170, Cursor = Cursors.Hand };
    readonly TextBlock timer = Mono("", 15, "FDim");
    bool copied;

    public DictatePage(AppModel model)
    {
        this.model = model;
        Focusable = false;
        orb.MouseLeftButtonUp += (_, _) => OrbClicked();
        System.Windows.Automation.AutomationProperties.SetName(orb, "Start dictation");
        model.PropertyChanged += OnModel;
        D.PropertyChanged += OnDictation;
        model.ThemeChanged += () => orb.Palette = model.Palette;
        Build();
    }

    void OrbClicked()
    {
        if (D.Phase == DictationPhase.Transcribing) return;
        if (D.IsLive) D.Stop(); else D.Start(DictationSource.InApp, null);
    }

    void OnModel(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppModel.Mic) or nameof(AppModel.HasApiKey) or nameof(AppModel.Snoozed)
            or nameof(AppModel.HistoryEnabled) or nameof(AppModel.WordsToday) or nameof(AppModel.ShortcutSummary)
            or nameof(AppModel.BubbleEnabled) or nameof(AppModel.Headline) or null)
            Build();
    }

    void OnDictation(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DictationController.ElapsedText): timer.Text = D.ElapsedText; break;
            case nameof(DictationController.Levels):
                orb.Level = D.Phase == DictationPhase.Recording ? D.Levels[^1] : 0; break;
            case nameof(DictationController.Phase) or nameof(DictationController.LastText) or nameof(DictationController.LastError):
                Build(); break;
        }
    }

    void Build()
    {
        var header = V(2, Eyebrow(DateTime.Now.ToString("dddd d MMMM")), Title(model.Headline, 30));

        orb.Palette = model.Palette;
        orb.Glyph = D.IsLive ? Glyph.Stop : Glyph.Mic;
        orb.Speed = D.Phase == DictationPhase.Recording ? 90 : 24;
        orb.Opacity = D.Phase == DictationPhase.Transcribing ? 0.75 : 1;
        System.Windows.Automation.AutomationProperties.SetName(orb, D.IsLive ? "Stop dictation" : "Start dictation");
        if (orb.Parent is Panel old) old.Children.Remove(orb);
        if (timer.Parent is Panel oldT) oldT.Children.Remove(timer);
        UIElement under;
        if (D.IsLive)
        {
            timer.Text = D.ElapsedText;
            under = V(10, timer.Also(t => t.HorizontalAlignment = HorizontalAlignment.Center),
                H(12, Button(D.Phase == DictationPhase.Paused ? "Resume" : "Pause", D.TogglePause), Button("Cancel", D.Cancel, "Danger"))
                    .Also(h => h.HorizontalAlignment = HorizontalAlignment.Center));
        }
        else if (D.Phase == DictationPhase.Transcribing)
            under = Body("Writing it up…", 15);
        else
            under = Body("Click the orb to try it here, or dictate in any app", 15);
        if (under is FrameworkElement u) u.HorizontalAlignment = HorizontalAlignment.Center;
        orb.HorizontalAlignment = HorizontalAlignment.Center;
        var orbArea = V(14, orb, under);
        orbArea.Margin = new Thickness(0, 8, 0, 8);

        Content = Scroll(Column(V(18, header, Blocker(), orbArea, Result(), Error(), Controls(), Today()), 760, new Thickness(28)));
    }

    UIElement? Blocker()
    {
        (string, string, Action)? b =
            model.Mic == Recorder.MicState.BlockedByWindows ? ("Windows is blocking the microphone for desktop apps", "Open settings", () => Open("ms-settings:privacy-microphone"))
            : model.Mic == Recorder.MicState.NoDevice ? ("No microphone found. Plug one in or turn it on", "Sound settings", () => Open("ms-settings:sound"))
            : !model.HasApiKey ? ("Add your Gemini API key to start dictating", "Add key", () => model.Tab = Tab.Settings)
            : null;
        if (b is not { } x) return null;
        return Card(Row(H(12, Icon(Glyph.Warning, 18, "FVoice"), Body(x.Item1, 15, "FInk").Also(t => t.VerticalAlignment = VerticalAlignment.Center)),
            Button(x.Item2, x.Item3)));
    }

    UIElement? Result()
    {
        if (D.LastText is not { } text) return null;
        var body = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent, FontSize = 17, Padding = new Thickness(0),
        };
        var copy = IconButton(copied ? Glyph.Check : Glyph.Copy, copied ? "Copied" : "Copy", async () =>
        {
            await TextInserter.SetClipboardAsync(text, transient: false);
            copied = true; Build();
            await System.Threading.Tasks.Task.Delay(1500);
            copied = false; Build();
        });
        return Card(V(12, Eyebrow("Last dictation"), body, Row(copy.Also(c => c.HorizontalAlignment = HorizontalAlignment.Left),
            IconButton(Glyph.Close, "Clear", D.ClearLast))));
    }

    UIElement? Error()
    {
        if (D.LastError is not { } error) return null;
        UIElement? action = error.Kind switch
        {
            TranscriptionErrorKind.NoApiKey or TranscriptionErrorKind.InvalidApiKey => Button("Open Settings", () => model.Tab = Tab.Settings),
            TranscriptionErrorKind.MicPermission => Button("Open microphone settings", () => Open("ms-settings:privacy-microphone")),
            _ => null,
        };
        return Card(Row(H(12, Icon(Glyph.Error, 17, "FDanger"), Body(error.UserMessage, 15, "FInk").Also(t => t.VerticalAlignment = VerticalAlignment.Center)), action));
    }

    UIElement Controls()
    {
        var bubble = Switch(Labeled("Floating bubble", "Appears next to the text box you are typing in"), model.BubbleEnabled,
            on => model.BubbleEnabled = on, "Floating bubble");
        var shortcuts = Labeled("Shortcuts", model.ShortcutSummary);
        UIElement snoozeAction;
        if (model.Snoozed) snoozeAction = Button("Resume", model.Resume);
        else
        {
            var combo = Combo(Enum.GetValues<SnoozeChoice>().Select(c => ((SnoozeChoice?)c, "Snooze " + c.Label().ToLowerInvariant()))
                .Prepend(((SnoozeChoice?)null, "Snooze…")), null, c => { if (c is { } choice) model.Snooze(choice); }, 220);
            System.Windows.Automation.AutomationProperties.SetName(combo, "Snooze");
            snoozeAction = combo;
        }
        var snooze = Row(Labeled("Snooze", model.Snoozed ? model.SnoozeLabel : "Hide the bubble and pause the shortcuts for a while"), snoozeAction);
        return Card(V(0, bubble, Divider(), shortcuts, Divider(), snooze));
    }

    UIElement Today()
    {
        if (!model.HistoryEnabled)
            return Card(V(4, Title("History is off", 16), Body("Turn it on in Settings to keep transcripts and daily totals on this PC", 14)));
        static UIElement Stat(string v, string l) => V(0, Title(v, 30), Mono(l));
        return Card(Row(Stat(model.WordsToday.ToString(), "words today"), Stat(model.DictationsToday.ToString(), "dictations")));
    }
}
