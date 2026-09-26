using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Fluent.Core;
using static Fluent.Ui;

namespace Fluent.Views;

/// <summary>Themed modal sheets (Mac: confirmationDialog and the transcript sheet).</summary>
static class Dialogs
{
    static Window Sheet(Window? owner, double width, double height, UIElement content, string title)
    {
        var w = new Window
        {
            Owner = owner, Width = width, Height = height, Title = title, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Content = new Border { Padding = new Thickness(24), Child = content },
        };
        w.SetResourceReference(Window.BackgroundProperty, "FBg");
        w.SourceInitialized += (_, _) =>
        {
            var p = (App.Current as App)?.Model?.Palette ?? Palette.Aurora;
            Native.StyleTitleBar(new WindowInteropHelper(w).Handle, !p.IsLight, p.Background, p.Ink);
        };
        return w;
    }

    public static bool Confirm(Window? owner, string question, string detail, string action)
    {
        var result = false;
        Window? w = null;
        var buttons = H(10,
            Button("Cancel", () => w!.Close()),
            Button(action, () => { result = true; w!.Close(); }, "Danger"));
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        w = Sheet(owner, 440, 210, new DockPanel
        {
            Children =
            {
                buttons.Also(b => DockPanel.SetDock(b, Dock.Bottom)),
                V(8, Title(question, 18), Body(detail, 14)),
            },
        }, "Fluent");
        w.ShowDialog();
        return result;
    }

    public static void ShowTranscript(Window? owner, AppModel model, Transcript t)
    {
        Window? w = null;
        var text = new TextBox
        {
            Text = t.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 16,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
        };
        var bottom = Row(
            Button("Delete", () =>
            {
                if (Confirm(w, "Delete this transcript?", "It is removed from this PC.", "Delete")) { model.Delete(t); w!.Close(); }
            }, "Danger").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left),
            H(10, Button("Copy", async () => await TextInserter.SetClipboardAsync(t.Text, false)),
                Button("Done", () => w!.Close(), "ButtonBase").Also(b => b.IsDefault = true)));
        var dock = new DockPanel();
        var date = Mono(t.CreatedAt.LocalDateTime.ToString("dddd d MMMM yyyy, t"), 12);
        date.Margin = new Thickness(0, 0, 0, 12);
        var stats = Mono($"{t.WordCount} words · {Math.Round(t.DurationSeconds)} s", 12);
        stats.Margin = new Thickness(0, 12, 0, 12);
        DockPanel.SetDock(date, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(stats, Dock.Bottom);
        dock.Children.Add(date);
        dock.Children.Add(bottom);
        dock.Children.Add(stats);
        dock.Children.Add(text);
        w = Sheet(owner, 560, 400, dock, "Transcript");
        w.ShowDialog();
    }
}
