using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Fluent.Core;
using static Fluent.Ui;

namespace Fluent.Views;

/// <summary>Every setting, grouped as on the Mac (which follows Android's Settings screen), opening with
/// "How to talk".</summary>
public sealed class SettingsPage : ContentControl
{
    readonly AppModel model;
    bool editingKey;
    string? testResult;
    bool testing;
    ScrollViewer? scroll;

    static readonly string[] Structural =
    [
        nameof(AppModel.HasApiKey), nameof(AppModel.Mic), nameof(AppModel.Snoozed), nameof(AppModel.HoldKey),
        nameof(AppModel.ToggleShortcut), nameof(AppModel.BubbleEnabled), nameof(AppModel.ThemeId), nameof(AppModel.Mode),
        nameof(AppModel.LaunchAtLoginEnabled), nameof(AppModel.OverlayThemeId),
    ];

    public SettingsPage(AppModel model)
    {
        this.model = model;
        Focusable = false;
        model.PropertyChanged += (_, e) => { if (e.PropertyName is { } n && Structural.Contains(n)) Build(); };
        model.Vocabulary.CollectionChanged += (_, _) => Build();
        model.ExcludedApps.CollectionChanged += (_, _) => Build();
        Build();
    }

    void Build()
    {
        var offset = scroll?.VerticalOffset ?? 0;
        var col = V(22,
            V(2, Eyebrow("Fluent for Windows"), Title("Settings", 30)),
            Section("How to talk", null, HowToTalk()),
            Section("Dictation", "Vocabulary: names, brands and jargon Gemini should spell your way.", Dictation()),
            Section("Shortcuts", "Hold the key, speak, and let go to insert. Esc cancels a dictation.", Shortcuts()),
            Section("Floating bubble", "Right-click the bubble for more: snooze, hide it in an app, or reset its position.", Bubble()),
            Section("Theme", null, Theme()),
            Section("Gemini API key", $"Stored encrypted for your Windows account (DPAPI). Streams to {Constants.LiveModel} while you talk.", ApiKey()),
            Section("Microphone", "Fluent opens the microphone only while you dictate.", Microphone()),
            Section("Privacy", "Off by default. History stays on this PC and holds text only. Audio is never saved.", Privacy()),
            Section("General", null, General()),
            Section("About", null, About()));
        scroll = Scroll(Column(col, 760, new Thickness(28)));
        Content = scroll;
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(offset);
    }

    static UIElement Section(string title, string? footer, UIElement body)
    {
        var card = Card(body);
        return V(8, Eyebrow(title), card, footer is null ? null : Body(footer, 12.5, "FFaint").Also(f => f.Margin = new Thickness(4, 0, 0, 0)));
    }

    static UIElement Line(string title, string? detail, UIElement? control) => Row(Labeled(title, detail, 14.5), control);

    static UIElement Stack(params UIElement?[] rows)
    {
        var list = rows.Where(r => r is not null).ToList();
        var v = new StackPanel();
        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0) v.Children.Add(Divider());
            v.Children.Add(list[i]!);
        }
        return v;
    }

    UIElement HowToTalk()
    {
        UIElement Step(int n, bool done, string title, string detail, UIElement? extra = null)
        {
            var badge = Badge(n, done && n == 1, 26, soft: true);
            badge.Margin = new Thickness(0, 0, 12, 0);
            var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            DockPanel.SetDock(badge, Dock.Left);
            d.Children.Add(badge);
            d.Children.Add(V(3, Title(title, 14), Body(detail, 12.5), extra));
            return d;
        }
        var steps = new StackPanel();
        steps.Children.Add(Step(1, model.HasApiKey,
            model.HasApiKey ? "Your Gemini key is saved" : "Add your free Gemini key",
            model.HasApiKey ? "You're ready to talk. Replace or test it under Gemini API key below."
                            : "Get one from Google AI Studio (free, about a minute), copy it, and paste it under Gemini API key below.",
            model.HasApiKey ? null : LinkText("Get a free key", Constants.ApiKeyUrl)));
        steps.Children.Add(Step(2, model.BubbleEnabled, "Click into any text box, then click the Fluent bubble",
            model.BubbleEnabled ? "Talk, then click it again (or Stop). Your words appear where you were typing."
                                : "The bubble is off. Turn it on under Floating bubble below."));
        var n = 3;
        if (model.HoldKey != HoldKey.Off)
            steps.Children.Add(Step(n++, true, $"Or hold {model.HoldKey.Label()} and talk", "Let go and it's typed. Esc cancels."));
        if (!model.ToggleShortcut.IsOff)
            steps.Children.Add(Step(n, true, $"Or press {model.ToggleShortcut.Label} to start, and again to stop", "Handy for long dictations."));
        return steps;
    }

    UIElement Dictation()
    {
        var mode = Combo(Enum.GetValues<TranscriptionMode>().Select(m => (m, m.Label())), model.Mode, m => model.Mode = m, 200);
        System.Windows.Automation.AutomationProperties.SetName(mode, "Mode");
        var lang = Combo(Languages.Options.Select(o => (o.Code, o.Name)), model.LanguageCode, c => model.LanguageCode = c, 260);
        System.Windows.Automation.AutomationProperties.SetName(lang, "Language");
        var word = new TextBox { Width = 200, Tag = "Add a name or word" };
        System.Windows.Automation.AutomationProperties.SetName(word, "Custom vocabulary word");
        void Add() { model.AddWord(word.Text); word.Text = ""; }
        word.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Add(); };
        var add = Button("Add", Add);
        var chips = new WrapPanel();
        foreach (var w in model.Vocabulary)
        {
            var x = new Button { Style = S("Quiet"), Content = "", FontFamily = Fonts.Icons, FontSize = 8, Padding = new Thickness(4, 2, 0, 2) };
            System.Windows.Automation.AutomationProperties.SetName(x, "Remove " + w);
            x.Click += (_, _) => model.RemoveWord(w);
            chips.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(9, 3, 5, 3), Margin = new Thickness(0, 0, 6, 6),
                Child = H(2, Body(w, 12.5, "FInk").Also(t => t.VerticalAlignment = VerticalAlignment.Center), x),
            }.Res(Border.BackgroundProperty, "FSurface").Res(Border.BorderBrushProperty, "FBorder"));
        }
        return Stack(
            Line("Mode", model.ModeDetail, mode),
            Line("Language", null, lang),
            V(10, Line("Custom vocabulary", null, H(8, word, add)), chips.Children.Count > 0 ? chips : null));
    }

    UIElement Shortcuts()
    {
        var hold = Combo(Enum.GetValues<HoldKey>().Select(k => (k, k.Label())), model.HoldKey, k => model.HoldKey = k, 220);
        System.Windows.Automation.AutomationProperties.SetName(hold, "Hold to talk");
        var toggle = Combo(ToggleShortcut.Presets.Append(ToggleShortcut.Off).Select(s => (s, s.Label)), model.ToggleShortcut, s => model.ToggleShortcut = s, 220);
        System.Windows.Automation.AutomationProperties.SetName(toggle, "Start and stop shortcut");
        return Stack(
            Line("Hold to talk", model.HoldKey == HoldKey.RightAlt ? "On keyboards where Right Alt is AltGr, pick Right Ctrl instead." : null, hold),
            Line("Start / stop shortcut", null, toggle),
            (App.Current as App)?.HotkeyProblem is { } problem ? Body(problem, 12.5, "FDanger") : null);
    }

    UIElement Bubble()
    {
        var size = new Slider { Minimum = 34, Maximum = 60, Value = model.BubbleSize };
        size.ValueChanged += (_, e) => model.BubbleSize = e.NewValue;
        System.Windows.Automation.AutomationProperties.SetName(size, "Bubble size");
        var opacity = new Slider { Minimum = 0.35, Maximum = 1, Value = model.BubbleOpacity };
        opacity.ValueChanged += (_, e) => model.BubbleOpacity = e.NewValue;
        System.Windows.Automation.AutomationProperties.SetName(opacity, "Bubble opacity");
        var length = Combo(Enum.GetValues<SnoozeChoice>().Select(c => (c, c.Label())), model.SnoozeChoice, c => model.SnoozeChoice = c, 220);
        System.Windows.Automation.AutomationProperties.SetName(length, "Snooze length");
        UIElement snooze = model.Snoozed
            ? H(10, Body(model.SnoozeLabel, 13).Also(t => t.VerticalAlignment = VerticalAlignment.Center), Button("Resume", model.Resume))
            : Button("Snooze now", () => model.Snooze());

        var addApp = new ComboBox { Width = 220 };
        System.Windows.Automation.AutomationProperties.SetName(addApp, "Hide the bubble in an app");
        addApp.Items.Add(new ComboBoxItem { Content = "Add app…", IsEnabled = false });
        addApp.SelectedIndex = 0;
        addApp.DropDownOpened += (_, _) =>
        {
            while (addApp.Items.Count > 1) addApp.Items.RemoveAt(1);
            foreach (var (process, name) in FieldFinder.RunningApps().Where(a => !model.ExcludedApps.Contains(a.Process)))
                addApp.Items.Add(new ComboBoxItem { Content = name, Tag = process });
        };
        addApp.SelectionChanged += (_, _) =>
        {
            if (addApp.SelectedItem is ComboBoxItem { Tag: string p }) Dispatcher.BeginInvoke(() => model.Exclude(p));
        };
        var hidden = new StackPanel();
        foreach (var p in model.ExcludedApps)
            hidden.Children.Add(Row(H(10, Body(p, 13.5, "FInk")), Button("Remove", () => model.Include(p), "Link")).Also(r => r.Margin = new Thickness(0, 6, 0, 0)));

        return Stack(
            Switch(Labeled("Show the bubble next to text boxes", null, 14.5), model.BubbleEnabled, on => model.BubbleEnabled = on, "Show the bubble next to text boxes"),
            Line("Size", null, size),
            Line("Opacity", null, opacity),
            Line("Snooze length", null, length),
            Line("Snooze", null, snooze),
            V(0, Line("Hidden in", model.ExcludedApps.Count == 0 ? "The bubble shows in every app" : null, addApp), hidden));
    }

    UIElement Theme()
    {
        var swatches = new WrapPanel();
        foreach (var t in Palette.All) swatches.Children.Add(Swatch(t));
        var overlay = Combo(Palette.All.Select(p => (p.Id, p.Name)).Prepend((Settings.MatchApp, "Match app theme")), model.OverlayThemeId,
            id => model.OverlayThemeId = id, 220);
        System.Windows.Automation.AutomationProperties.SetName(overlay, "Bubble and capsule colours");
        return Stack(swatches, Line("Bubble and capsule colours", null, overlay));
    }

    UIElement Swatch(Palette theme)
    {
        var selected = theme.Id == model.ThemeId;
        var tile = new Grid { Width = 64, Height = 64 };
        tile.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(theme.Background),
            BorderThickness = new Thickness(selected ? 2.5 : 1),
            BorderBrush = selected ? new SolidColorBrush(model.Palette.Accent) : new SolidColorBrush(Palette.WithAlpha(theme.Ink, 0.18)),
        });
        tile.Children.Add(new Ellipse
        {
            Width = 28, Height = 28, Fill = new ImageBrush(Orb.Conic(theme.Orb)),
            Effect = new BlurEffect { Radius = 2 },
        });
        var b = new Button
        {
            Style = S("Quiet"), Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 0),
            Content = V(4, tile, Body(theme.Name, 12, "FInk").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center),
                Body(theme.Tagline, 10.5, "FFaint").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center)),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, $"{theme.Name} theme{(selected ? " (selected)" : "")}");
        b.Click += (_, _) => model.ThemeId = theme.Id;
        return b;
    }

    UIElement ApiKey()
    {
        if (editingKey || !model.HasApiKey)
        {
            var box = new PasswordBox { Width = 320 };
            System.Windows.Automation.AutomationProperties.SetName(box, "Gemini API key");
            var save = Button("Save key", () => { model.SaveApiKey(box.Password); editingKey = false; testResult = null; Build(); });
            save.IsEnabled = false;
            box.PasswordChanged += (_, _) => save.IsEnabled = box.Password.Trim().Length > 0;
            return V(12, H(10, box, save, model.HasApiKey ? Button("Cancel", () => { editingKey = false; Build(); }) : null),
                LinkText("Get a free key from Google AI Studio", Constants.ApiKeyUrl));
        }
        var test = Button(testing ? "Testing…" : "Test connection", async () =>
        {
            testing = true; Build();
            var r = await model.TestApiKeyAsync();
            testResult = r.IsSuccess ? $"Connected to {r.Text}." : r.Error!.UserMessage;
            testing = false; Build();
        });
        test.IsEnabled = !testing;
        return V(12,
            Line("Key", null, Mono(model.MaskedKey, 12.5, "FDim")),
            Row(H(10, test, Button("Replace key", () => { editingKey = true; Build(); })),
                Button("Remove key", () => { model.SaveApiKey(""); testResult = null; }, "Danger")),
            testResult is null ? null : Body(testResult, 12.5));
    }

    UIElement Microphone()
    {
        var (text, color) = model.Mic switch
        {
            Recorder.MicState.Allowed => ("Allowed", "FSuccess"),
            Recorder.MicState.BlockedByWindows => ("Blocked by Windows privacy settings", "FDanger"),
            _ => ("No microphone found", "FDanger"),
        };
        var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center }.Res(Shape.FillProperty, color);
        return Row(H(10, dot, Body(text, 14, "FInk").Also(t => t.VerticalAlignment = VerticalAlignment.Center)),
            Button("Microphone settings", () => Open("ms-settings:privacy-microphone")));
    }

    UIElement Privacy() =>
        Switch(Labeled("Keep history", null, 14.5), model.HistoryEnabled, on => model.HistoryEnabled = on, "Keep history");

    UIElement General() => Stack(
        V(6, Switch(Labeled("Open Fluent when I sign in", "Starts quietly in the system tray", 14.5), model.LaunchAtLoginEnabled,
            on => model.LaunchAtLoginEnabled = on, "Open Fluent when I sign in"),
            model.LaunchAtLoginError is { } e ? Body(e, 12.5, "FDanger") : null),
        Switch(Labeled("Start and stop sounds", null, 14.5), model.SoundsEnabled, on => model.SoundsEnabled = on, "Start and stop sounds"));

    UIElement About()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return Stack(
            LinkText("Terms and Conditions", Constants.TermsUrl, 14),
            LinkText("Privacy Policy", Constants.PrivacyUrl, 14),
            Line("Version", null, Mono($"{version?.Major}.{version?.Minor}.{version?.Build}", 12.5, "FDim")),
            Line("Updates", "Fluent is free. New versions are on the website.", Button("Check the website", () => Open(Constants.WebsiteUrl))));
    }
}
