using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Fluent.Core;
using static Fluent.Ui;

namespace Fluent.Views;

/// <summary>Clickwrap gate, as on Android 1.8.1 and the Mac: one checkbox that links the Terms and the
/// Privacy Policy. Shown before setup, and again whenever <see cref="Constants.TermsVersion"/> changes.
/// Nothing works, including the bubble and the shortcuts, until it is accepted.</summary>
public sealed class TermsView : Grid
{
    public TermsView(AppModel model)
    {
        var returning = model.SetupComplete;
        var mark = new FluentMark { Width = 110, Height = 110, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.8, 0.8) }
            .Res(FluentMark.FillProperty, "FMarkFill");
        var head = V(0,
            mark,
            Title(returning ? "Our terms have changed" : "Welcome to Fluent", 34).Also(t => { t.Margin = new Thickness(0, 14, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center; }),
            Body(returning ? "Please accept the updated terms to keep using Fluent." : "Speak in any app. Fluent writes it for you.", 17)
                .Also(t => { t.Margin = new Thickness(0, 8, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center; }));
        head.HorizontalAlignment = HorizontalAlignment.Center;
        head.VerticalAlignment = VerticalAlignment.Center;
        mark.HorizontalAlignment = HorizontalAlignment.Center;

        var agree = new CheckBox { Style = S("Tick"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(agree, "I agree");
        var text = new TextBlock { Style = S("Body"), FontSize = 15, Margin = new Thickness(12, 0, 0, 0) };
        text.Inlines.Add(new Run("I agree to the "));
        text.Inlines.Add(Link("Terms and Conditions", Constants.TermsUrl));
        text.Inlines.Add(new Run(" and the "));
        text.Inlines.Add(Link("Privacy Policy", Constants.PrivacyUrl));
        text.Inlines.Add(new Run(", and I am 18 or older."));
        text.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not Hyperlink && e.OriginalSource is not Run { Parent: Hyperlink }) agree.IsChecked = !agree.IsChecked; };
        var agreeRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(agree, Dock.Left);
        agreeRow.Children.Add(agree);
        agreeRow.Children.Add(text);

        var go = Button("Continue", model.AcceptTerms, "Primary");
        go.IsEnabled = false;
        go.IsDefault = true;
        agree.Checked += (_, _) => go.IsEnabled = true;
        agree.Unchecked += (_, _) => go.IsEnabled = false;
        var bottom = V(18, agreeRow, go);
        bottom.MaxWidth = 440;
        bottom.Margin = new Thickness(32, 0, 32, 56);
        bottom.VerticalAlignment = VerticalAlignment.Bottom;

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(head);
        SetRow(bottom, 1);
        Children.Add(bottom);

        Loaded += (_, _) =>
        {
            var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease };
            mark.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            mark.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        };
    }
}
