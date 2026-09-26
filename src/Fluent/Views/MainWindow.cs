using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Fluent.Views;

/// <summary>The one app window: Terms, then setup, then the sidebar and the four screens (Mac: RootView).
/// Closing it only hides it; Fluent keeps running in the tray, like the Mac menu bar app.</summary>
public sealed class MainWindow : Window
{
    readonly AppModel model;
    readonly AmbientBackground ambient = new();
    readonly ContentControl host = new() { Focusable = false };
    string shown = "";
    MainView? main;
    public bool ReallyClose { get; set; }

    public MainWindow(AppModel model)
    {
        this.model = model;
        Title = "Fluent";
        Width = 1000; Height = 720;
        MinWidth = 880; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Fluent.ico"));
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        this.SetResourceReference(BackgroundProperty, "FBg");
        Content = new Grid { Children = { ambient, host } };

        model.PropertyChanged += OnModel;
        model.ThemeChanged += ApplyTheme;
        SourceInitialized += (_, _) => ApplyTheme();
        KeyDown += (_, e) => { if (e.Key == Key.Escape && model.Dictation.IsLive) model.Dictation.Cancel(); };
        ApplyTheme();
        ShowCurrent();
    }

    void OnModel(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppModel.TermsAccepted) or nameof(AppModel.SetupComplete) or null) ShowCurrent();
    }

    void ApplyTheme()
    {
        var p = model.Palette;
        ambient.SetPalette(p);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) Native.StyleTitleBar(hwnd, !p.IsLight, p.Background, p.Ink);
    }

    public void ShowCurrent()
    {
        var want = !model.TermsAccepted ? "terms" : !model.SetupComplete ? "setup" : "main";
        if (want == shown) return;
        shown = want;
        FrameworkElement view = want switch
        {
            "terms" => new TermsView(model),
            "setup" => new OnboardingView(model),
            _ => main ??= new MainView(model),
        };
        view.Opacity = 0;
        host.Content = view;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    public void ShowTab(Tab? tab)
    {
        if (tab is { } t) model.Tab = t;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false;   // bring it in front of the app the user was in
        Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ReallyClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
