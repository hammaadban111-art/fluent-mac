using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace Fluent;

/// <summary>Small builders so the screens read top to bottom like the Mac's SwiftUI views. Colours are
/// always resource references (see <see cref="ThemeResources"/>), so a theme change repaints in place.</summary>
static class Ui
{
    public static T Res<T>(this T el, DependencyProperty p, string key) where T : FrameworkElement
    {
        el.SetResourceReference(p, key);
        return el;
    }

    public static Style S(string key) => (Style)Application.Current.FindResource(key);

    public static TextBlock Title(string text, double size, string color = "FInk") =>
        new TextBlock { Text = text, FontSize = size, Style = S("Title") }.Res(TextBlock.ForegroundProperty, color);

    public static TextBlock Body(string text, double size = 14, string color = "FDim") =>
        new TextBlock { Text = text, FontSize = size, Style = S("Body") }.Res(TextBlock.ForegroundProperty, color);

    public static TextBlock Mono(string text, double size = 11, string color = "FFaint") =>
        new TextBlock { Text = text, FontSize = size, Style = S("Mono") }.Res(TextBlock.ForegroundProperty, color);

    public static TextBlock Eyebrow(string text) =>
        new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11, Style = S("Mono"), Margin = new Thickness(4, 0, 0, 0) }
            .Res(TextBlock.ForegroundProperty, "FFaint").Spaced();

    static TextBlock Spaced(this TextBlock t) { t.FontStretch = FontStretches.Normal; return t; }

    public static TextBlock Icon(string glyph, double size = 16, string color = "FAccent") =>
        new TextBlock { Text = glyph, FontSize = size, Style = S("Icon") }.Res(TextBlock.ForegroundProperty, color);

    public static Border Card(UIElement content, double padding = 18) =>
        new() { Style = S("Card"), Child = content, Padding = new Thickness(padding) };

    public static StackPanel V(double spacing, params UIElement?[] children) => Stack(Orientation.Vertical, spacing, children);
    public static StackPanel H(double spacing, params UIElement?[] children) => Stack(Orientation.Horizontal, spacing, children);

    static StackPanel Stack(Orientation o, double spacing, UIElement?[] children)
    {
        var p = new StackPanel { Orientation = o };
        var first = true;
        foreach (var c in children)
        {
            if (c is null) continue;
            if (!first && c is FrameworkElement fe)
            {
                var m = fe.Margin;
                fe.Margin = o == Orientation.Vertical ? new Thickness(m.Left, m.Top + spacing, m.Right, m.Bottom)
                                                      : new Thickness(m.Left + spacing, m.Top, m.Right, m.Bottom);
            }
            p.Children.Add(c);
            first = false;
        }
        return p;
    }

    /// <summary>Left content stretches, right content hugs the edge.</summary>
    public static Grid Row(UIElement left, UIElement? right, double gap = 12)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(left);
        if (right is FrameworkElement r)
        {
            Grid.SetColumn(r, 1);
            r.Margin = new Thickness(gap, 0, 0, 0);
            r.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(r);
        }
        return g;
    }

    public static Button Button(string text, Action onClick, string style = "ButtonBase", string? name = null)
    {
        var b = new Button { Content = text, Style = S(style) };
        if (name is not null) System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button IconButton(string glyph, string text, Action onClick, string style = "ButtonBase")
    {
        var b = new Button { Style = S(style), Content = H(7, Icon(glyph, 13, "FInk").Also(t => t.SetBinding(TextBlock.ForegroundProperty,
            new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) })),
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }) };
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static T Also<T>(this T t, Action<T> f) { f(t); return t; }

    public static CheckBox Switch(UIElement label, bool on, Action<bool> changed, string? name = null)
    {
        var c = new CheckBox { Style = S("Switch"), Content = label, IsChecked = on };
        if (name is not null) System.Windows.Automation.AutomationProperties.SetName(c, name);
        c.Checked += (_, _) => changed(true);
        c.Unchecked += (_, _) => changed(false);
        return c;
    }

    public static StackPanel Labeled(string title, string? detail, double titleSize = 15.5) =>
        V(2, Title(title, titleSize), detail is null ? null : Body(detail, 13));

    public static ComboBox Combo<T>(System.Collections.Generic.IEnumerable<(T Value, string Label)> items, T selected, Action<T> changed, double width = 240)
    {
        var c = new ComboBox { Width = width };
        var index = 0;
        foreach (var (v, l) in items)
        {
            c.Items.Add(new ComboBoxItem { Content = l, Tag = v });
            if (Equals(v, selected)) c.SelectedIndex = index;
            index++;
        }
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: T v }) changed(v); };
        return c;
    }

    public static Border Divider() => new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) }.Res(Border.BackgroundProperty, "FBorder");

    public static Hyperlink Link(string text, string url)
    {
        var h = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url), TextDecorations = TextDecorations.Underline };
        h.SetResourceReference(TextElement.ForegroundProperty, "FAccent");
        h.RequestNavigate += (_, e) => Open(e.Uri.AbsoluteUri);
        return h;
    }

    public static TextBlock LinkText(string text, string url, double size = 13.5)
    {
        var t = new TextBlock { FontSize = size, Style = S("Body") };
        t.Inlines.Add(Link(text, url));
        return t;
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    /// <summary>A round numbered or ticked badge (onboarding steps, Settings → How to talk).</summary>
    public static Grid Badge(int n, bool done, double size = 28, bool soft = false)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top };
        var circle = new System.Windows.Shapes.Ellipse();
        if (done) circle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, soft ? "FSuccessSoft" : "FSuccess");
        else
        {
            circle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, soft ? "FAccentSoft" : "FSurface");
            if (!soft) { circle.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "FBorder"); circle.StrokeThickness = 1; }
        }
        g.Children.Add(circle);
        UIElement mark = done
            ? Icon(Glyph.Check, size * 0.45, soft ? "FSuccess" : "FOnAccent")
            : Title(n.ToString(), size * 0.48, soft ? "FAccent" : "FDim");
        if (mark is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        if (done && !soft && mark is TextBlock tb) tb.Foreground = Brushes.White;
        g.Children.Add(mark);
        return g;
    }

    public static ScrollViewer Scroll(UIElement content) =>
        new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };

    /// <summary>Centres a column of at most <paramref name="max"/> DIPs, like the Mac's frame(maxWidth:).</summary>
    /// (WPF centres a Stretch element whose MaxWidth is smaller than the space it gets.)
    public static FrameworkElement Column(UIElement content, double max, Thickness padding) =>
        new Border { Child = content, MaxWidth = max + padding.Left + padding.Right, Padding = padding, HorizontalAlignment = HorizontalAlignment.Stretch };
}
