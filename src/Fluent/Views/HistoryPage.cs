using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Fluent.Core;
using static Fluent.Ui;

namespace Fluent.Views;

/// <summary>Transcripts kept on this PC, grouped by day, searchable (Mac: HistoryView).</summary>
public sealed class HistoryPage : Grid
{
    readonly AppModel model;
    readonly TextBox search = new() { Width = 230, Tag = "Search transcripts" };
    readonly ContentControl list = new() { Focusable = false };
    readonly StackPanel tools;

    public HistoryPage(AppModel model)
    {
        this.model = model;
        Margin = new Thickness(28);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        System.Windows.Automation.AutomationProperties.SetName(search, "Search transcripts");
        search.TextChanged += (_, _) => BuildList();
        tools = H(10, search, Button("Clear all", ConfirmClear, "Danger"));
        var header = Row(V(2, Eyebrow("On this PC only"), Title("History", 30)), tools);
        header.Margin = new Thickness(0, 0, 0, 16);
        Children.Add(header);
        SetRow(list, 1);
        Children.Add(list);
        model.History.CollectionChanged += (_, _) => BuildList();
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppModel.HistoryEnabled)) BuildList(); };
        BuildList();
    }

    void BuildList()
    {
        tools.Visibility = model.History.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (!model.HistoryEnabled && model.History.Count == 0)
        {
            list.Content = Empty("History is off", "Transcripts are only kept on this PC when you turn history on.",
                Button("Turn on history", () => model.HistoryEnabled = true, "Primary"));
            return;
        }
        if (model.History.Count == 0) { list.Content = Empty("Nothing yet", "Your dictations will appear here.", null); return; }

        var q = search.Text.Trim();
        var items = q.Length == 0 ? model.History.ToList()
            : model.History.Where(t => t.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        var days = items.GroupBy(t => t.CreatedAt.LocalDateTime.Date).OrderByDescending(g => g.Key);
        var col = new StackPanel();
        foreach (var day in days)
        {
            col.Children.Add(Eyebrow(DayTitle(day.Key)).Also(t => t.Margin = new Thickness(4, col.Children.Count == 0 ? 0 : 18, 0, 8)));
            foreach (var t in day) col.Children.Add(RowFor(t));
        }
        if (items.Count == 0) col.Children.Add(Body("No transcripts match.", 15));
        list.Content = Scroll(col);
    }

    UIElement RowFor(Transcript t)
    {
        var text = Body(t.Text, 15, "FInk");
        text.MaxHeight = 64;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        var card = Card(V(6, text, Mono($"{t.CreatedAt.LocalDateTime:t} · {t.WordCount} words")), 14);
        card.Margin = new Thickness(0, 0, 0, 8);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, _) => Detail(t);
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Copy" }.Also(m => m.Click += async (_, _) => await TextInserter.SetClipboardAsync(t.Text, false)));
        menu.Items.Add(new MenuItem { Header = "Delete" }.Also(m => m.Click += (_, _) => model.Delete(t)));
        card.ContextMenu = menu;
        return card;
    }

    static string DayTitle(DateTime d) =>
        d == DateTime.Today ? "Today" : d == DateTime.Today.AddDays(-1) ? "Yesterday" : d.ToString("dddd d MMMM");

    static UIElement Empty(string title, string detail, UIElement? action)
    {
        var v = V(10,
            Icon(Glyph.Clock, 38, "FFaint").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center),
            Title(title, 20).Also(i => i.HorizontalAlignment = HorizontalAlignment.Center),
            Body(detail, 15).Also(i => { i.HorizontalAlignment = HorizontalAlignment.Center; i.TextAlignment = TextAlignment.Center; }),
            action?.Also(a => { if (a is FrameworkElement f) { f.HorizontalAlignment = HorizontalAlignment.Center; f.Margin = new Thickness(0, 16, 0, 0); } }));
        v.VerticalAlignment = VerticalAlignment.Center;
        v.HorizontalAlignment = HorizontalAlignment.Center;
        return v;
    }

    void ConfirmClear()
    {
        if (Dialogs.Confirm(Window.GetWindow(this), "Delete every transcript on this PC?", "This can't be undone.", "Delete all"))
            model.ClearHistory();
    }

    void Detail(Transcript t) => Dialogs.ShowTranscript(Window.GetWindow(this), model, t);
}
