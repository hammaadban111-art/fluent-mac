using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Fluent.Core;
using static Fluent.Ui;

namespace Fluent.Views;

/// <summary>Setup in a fixed order, as on Android and the Mac: each step shows its live state, and the
/// microphone switch is detected the moment it changes in Windows Settings. Windows needs no
/// Accessibility permission, so there are two steps instead of the Mac's three.</summary>
public sealed class OnboardingView : ContentControl
{
    readonly AppModel model;
    string? testResult;
    bool testing;

    public OnboardingView(AppModel model)
    {
        this.model = model;
        Focusable = false;
        model.PropertyChanged += OnChange;
        Unloaded += (_, _) => model.PropertyChanged -= OnChange;
        Loaded += (_, _) => { model.PropertyChanged -= OnChange; model.PropertyChanged += OnChange; };
        Build();
    }

    void OnChange(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppModel.Mic) or nameof(AppModel.HasApiKey) or nameof(AppModel.HoldKey) or nameof(AppModel.ToggleShortcut)) Build();
    }

    void Build()
    {
        var header = H(14,
            new FluentMark { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center }.Res(FluentMark.FillProperty, "FMarkFill"),
            V(2, Title("Set up Fluent", 30), Body("Two steps and you can talk into any app on your PC.", 15)));

        var micDetail = model.Mic switch
        {
            Recorder.MicState.BlockedByWindows => "Windows is blocking the microphone. In Settings › Privacy & security › Microphone, turn on \"Microphone access\" and \"Let desktop apps access your microphone\".",
            Recorder.MicState.NoDevice => "No microphone was found. Plug one in or turn it on, and this step ticks itself.",
            _ => "Fluent listens only while you dictate. Audio goes to Gemini and is never saved.",
        };
        var mic = Step(1, "Microphone", model.MicAllowed, micDetail,
            model.MicAllowed ? null : Button("Open microphone settings", () => Open("ms-settings:privacy-microphone")));

        UIElement keyActions;
        if (model.HasApiKey)
        {
            var test = Button(testing ? "Testing…" : "Test connection", Test);
            test.IsEnabled = !testing;
            keyActions = H(12, Mono(model.MaskedKey, 13, "FDim").Also(t => t.VerticalAlignment = VerticalAlignment.Center), test,
                testResult is null ? null : Body(testResult, 13).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        }
        else
        {
            var box = new PasswordBox { Width = 300 };
            System.Windows.Automation.AutomationProperties.SetName(box, "Gemini API key");
            var save = Button("Save key", () => model.SaveApiKey(box.Password));
            save.IsEnabled = false;
            box.PasswordChanged += (_, _) => save.IsEnabled = box.Password.Trim().Length > 0;
            keyActions = H(10, box, save, LinkText("Get a free key", Constants.ApiKeyUrl).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        }
        var key = Step(2, "Gemini API key", model.HasApiKey,
            "Fluent uses your own free key from Google AI Studio. It is stored encrypted for your Windows account.", keyActions, alwaysShow: true);

        var how = Card(V(8,
            Title("How to dictate", 17),
            HowTo(Glyph.Touch, "Click into any text box. A Fluent bubble appears next to it; click it, speak, click it again (or Stop)."),
            HowTo(Glyph.Keyboard, $"Or hold {model.HoldKey.Label()}, speak, and let go to insert."),
            HowTo(Glyph.Keyboard, $"Or press {model.ToggleShortcut.Label} to start, and again to stop.")));

        var later = Button("Finish later", () => model.SetupComplete = true, "Quiet");
        var finish = Button("Finish setup", () => model.SetupComplete = true, "Primary");
        finish.Width = 260;
        finish.IsEnabled = model.Ready;
        var bottom = Row(later.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left), finish);

        Content = Scroll(Column(V(16, header, mic, key, how, bottom), 720, new Thickness(36)));
    }

    async void Test()
    {
        testing = true; Build();
        var r = await model.TestApiKeyAsync();
        testResult = r.IsSuccess ? $"Connected to {r.Text}." : r.Error!.UserMessage;
        testing = false; Build();
    }

    static UIElement HowTo(string glyph, string text) =>
        new DockPanel
        {
            Children =
            {
                Icon(glyph, 15).Also(i => { i.Width = 24; i.VerticalAlignment = VerticalAlignment.Top; i.Margin = new Thickness(0, 2, 6, 0); DockPanel.SetDock(i, Dock.Left); }),
                Body(text, 14),
            },
        };

    static Border Step(int n, string title, bool done, string detail, UIElement? actions, bool alwaysShow = false)
    {
        var status = Mono(done ? "Done" : "Needed", 11, done ? "FSuccess" : "FFaint");
        var right = V(8, Row(Title(title, 17), status), Body(detail, 14), done && !alwaysShow ? null : actions);
        var dock = new DockPanel();
        var badge = Badge(n, done);
        badge.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(badge, Dock.Left);
        dock.Children.Add(badge);
        dock.Children.Add(right);
        var card = Card(dock);
        System.Windows.Automation.AutomationProperties.SetName(card, $"{title}: {(done ? "done" : "needed")}");
        return card;
    }
}
