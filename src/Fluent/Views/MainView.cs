using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using static Fluent.Ui;

namespace Fluent.Views;

/// <summary>Sidebar plus the four screens (Mac: MainView).</summary>
public sealed class MainView : Grid
{
    readonly AppModel model;
    readonly ContentControl page = new() { Focusable = false };
    readonly RadioButton[] tabs;
    readonly Border chip = new() { CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
    DictatePage? dictate; HistoryPage? history; StylePage? style; SettingsPage? settings;

    public MainView(AppModel model)
    {
        this.model = model;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var brand = H(10,
            new FluentMark { Width = 30, Height = 30 }.Res(FluentMark.FillProperty, "FMarkFill"),
            Title("Fluent", 22).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        brand.Margin = new Thickness(6, 4, 0, 18);

        (Tab Tab, string Glyph)[] items = [(Tab.Dictate, Glyph.Mic), (Tab.History, Glyph.Clock), (Tab.Style, Glyph.Font), (Tab.Settings, Glyph.Settings)];
        tabs = new RadioButton[items.Length];
        var nav = new StackPanel();
        for (var i = 0; i < items.Length; i++)
        {
            var (tab, glyph) = items[i];
            var rb = new RadioButton { Content = tab.ToString(), Tag = glyph, Style = S("Nav"), GroupName = "nav" };
            System.Windows.Automation.AutomationProperties.SetName(rb, tab.ToString());
            rb.Checked += (_, _) => model.Tab = tab;
            tabs[i] = rb;
            nav.Children.Add(rb);
        }

        chip.Res(Border.BackgroundProperty, "FSurface").Res(Border.BorderBrushProperty, "FBorder");
        chip.MouseLeftButtonUp += (_, _) => model.Tab = Tab.Settings;
        chip.HorizontalAlignment = HorizontalAlignment.Left;

        var side = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(brand, Dock.Top);
        DockPanel.SetDock(nav, Dock.Top);
        DockPanel.SetDock(chip, Dock.Bottom);
        side.Children.Add(brand);
        side.Children.Add(nav);
        side.Children.Add(chip);
        side.Children.Add(new Border());
        var sideBg = new Border { Child = side, BorderThickness = new Thickness(0, 0, 1, 0) }
            .Res(Border.BackgroundProperty, "FSidebar").Res(Border.BorderBrushProperty, "FBorder");
        Children.Add(sideBg);
        SetColumn(page, 1);
        Children.Add(page);

        model.TabChanged += ShowTab;
        model.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(AppModel.Status) or null) UpdateChip(); };
        UpdateChip();
        ShowTab();
    }

    void ShowTab()
    {
        tabs[(int)model.Tab].IsChecked = true;
        page.Content = model.Tab switch
        {
            Tab.Dictate => dictate ??= new DictatePage(model),
            Tab.History => history ??= new HistoryPage(model),
            Tab.Style => style ??= new StylePage(model),
            _ => settings ??= new SettingsPage(model),
        };
    }

    void UpdateChip()
    {
        var (text, kind) = model.Status;
        var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center }
            .Res(Shape.FillProperty, kind switch { "ok" => "FSuccess", "danger" => "FDanger", "warn" => "FVoice", _ => "FFaint" });
        chip.Child = H(8, dot, Mono(text, 11, "FDim").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        System.Windows.Automation.AutomationProperties.SetName(chip, "Status: " + text);
    }
}
