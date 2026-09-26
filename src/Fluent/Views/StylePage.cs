using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Fluent.Core;
using static Fluent.Ui;

namespace Fluent.Views;

/// <summary>Per-app styles, as on Android 1.7 and the Mac: Fluent matches how you write where you are
/// writing, from the app in front. Styles only change capitals, punctuation and layout; words are never
/// rewritten.</summary>
public sealed class StylePage : ContentControl
{
    readonly AppModel model;
    StyleCategory category = StyleCategory.Personal;

    public StylePage(AppModel model)
    {
        this.model = model;
        Focusable = false;
        model.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(AppModel.Mode) or nameof(AppModel.StyleEnabled) or "Styles") Build(); };
        Build();
    }

    public StyleCategory Category { get => category; set { category = value; Build(); } }

    void Build()
    {
        var header = V(2, Eyebrow("Per app"), Title("Style", 30));
        var intro = Body("Fluent matches how you write where you are writing. Styles only change capitals, punctuation and layout. Your words are never rewritten.", 15);
        var toggle = Card(Switch(Labeled("Match my style", model.StyleNote), model.StyleEnabled, on => model.StyleEnabled = on, "Match my style"));

        var seg = new UniformGrid4();
        foreach (var c in Enum.GetValues<StyleCategory>())
        {
            var rb = new RadioButton { Content = c.Label(), Style = S("Segment"), GroupName = "cat", IsChecked = c == category };
            System.Windows.Automation.AutomationProperties.SetName(rb, c.Label());
            rb.Checked += (_, _) => { if (category != c) Category = c; };
            seg.Children.Add(rb);
        }
        var segBox = new Border { Child = seg, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(2) }
            .Res(Border.BackgroundProperty, "FInkSofter").Res(Border.BorderBrushProperty, "FBorder");

        var cards = category.Styles().Select(StyleCard).ToArray();
        var body = V(16, [header, intro, toggle, segBox,
            Title($"How do you write your {category.Question()}?", 19),
            Body($"This style applies in {AppCategories.AppliesIn(category)}", 14).Also(b => b.Margin = new Thickness(0, -8, 0, 0)),
            .. cards]);
        body.Opacity = model.StyleEnabled ? 1 : 0.5;
        Content = Scroll(Column(body, 760, new Thickness(28)));
    }

    UIElement StyleCard(WritingStyle style)
    {
        var selected = model.StyleFor(category) == style;
        var tick = Icon(selected ? "" : "", 18, selected ? "FAccent" : "FFaint");
        var card = Card(V(10,
            Row(Title(style.Title(), 18), tick),
            Mono(style.Rule().ToUpperInvariant()),
            Body(StyleFormatter.Format(category.Sample(), style, category), 15)));
        if (selected) { card.SetResourceReference(Border.BorderBrushProperty, "FAccent"); card.BorderThickness = new Thickness(2); card.Padding = new Thickness(17); }
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.IsEnabled = model.StyleEnabled;
        System.Windows.Automation.AutomationProperties.SetName(card, style.Title() + (selected ? " (selected)" : ""));
        card.MouseLeftButtonUp += (_, _) => { if (model.StyleEnabled) model.SetStyle(style, category); };
        return card;
    }

    sealed class UniformGrid4 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid4() { Rows = 1; }
    }
}
