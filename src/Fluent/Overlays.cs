using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Fluent.Core;

namespace Fluent;

/// <summary>A borderless, always-on-top window that never takes focus. Clicking it leaves the text box
/// the user was typing in focused (Android: FLAG_NOT_FOCUSABLE; Mac: non-activating NSPanel).</summary>
public class OverlayWindow : Window
{
    public IntPtr Handle { get; private set; }

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        UseLayoutRounding = true;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Native.MakeNoActivate(Handle);
            HwndSource.FromHwnd(Handle)?.AddHook(Hook);
        };
    }

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(Native.MA_NOACTIVATE); }
        return IntPtr.Zero;
    }

    public void EnsureHandle()
    {
        if (Handle == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>Places the window's top-left at a screen pixel position and shows it without activating.</summary>
    public void MoveTo(double x, double y)
    {
        EnsureHandle();
        if (!IsVisible) Show();
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, (int)Math.Round(x), (int)Math.Round(y), 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    public (int X, int Y, int W, int H) PixelRect()
    {
        Native.GetWindowRect(Handle, out var r);
        return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}

/// <summary>The floating bubble next to a text box: click to dictate, press and hold to talk, drag to
/// move, right-click for the menu.</summary>
public sealed class BubbleWindow : OverlayWindow
{
    public Action? OnClick, OnHoldStart, OnHoldEnd;
    public Action<int, int>? OnDragEnd;
    public Func<ContextMenu?>? MenuProvider;
    public bool Dragging { get; private set; }

    readonly Orb orb = new() { ForceGlass = true, Speed = 30 };
    readonly Grid root = new();
    readonly ScaleTransform press = new(1, 1);
    readonly DispatcherTimer holdTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    Native.POINT downAt;
    (int X, int Y, int W, int H) originAtDown;
    bool holdFired, down;

    public BubbleWindow()
    {
        Title = "Fluent bubble";
        root.Children.Add(orb);
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        root.RenderTransform = press;
        root.Background = Brushes.Transparent;
        Content = root;
        orb.Cursor = Cursors.Hand;
        System.Windows.Automation.AutomationProperties.SetName(orb, "Fluent: dictate into this field");
        holdTimer.Tick += (_, _) =>
        {
            holdTimer.Stop();
            if (Dragging || !down) return;
            holdFired = true;
            OnHoldStart?.Invoke();
        };
        orb.MouseLeftButtonDown += (_, e) =>
        {
            Native.GetCursorPos(out downAt);
            originAtDown = PixelRect();
            Dragging = false; holdFired = false; down = true;
            SetPressed(true);
            orb.CaptureMouse();
            holdTimer.Start();
            e.Handled = true;
        };
        orb.MouseMove += (_, _) =>
        {
            if (!down) return;
            Native.GetCursorPos(out var now);
            int dx = now.X - downAt.X, dy = now.Y - downAt.Y;
            if (!Dragging && !holdFired && Math.Sqrt(dx * dx + dy * dy) > 4) { Dragging = true; holdTimer.Stop(); }
            if (Dragging) MoveTo(originAtDown.X + dx, originAtDown.Y + dy);
        };
        orb.MouseLeftButtonUp += (_, e) =>
        {
            if (!down) return;
            down = false;
            holdTimer.Stop();
            orb.ReleaseMouseCapture();
            SetPressed(false);
            if (holdFired) OnHoldEnd?.Invoke();
            else if (Dragging) { var r = PixelRect(); OnDragEnd?.Invoke(r.X, r.Y); }
            else OnClick?.Invoke();
            Dragging = false; holdFired = false;
            e.Handled = true;
        };
        orb.MouseRightButtonUp += (_, e) =>
        {
            if (MenuProvider?.Invoke() is { } menu)
            {
                menu.PlacementTarget = orb;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            }
            e.Handled = true;
        };
    }

    void SetPressed(bool on)
    {
        var to = on ? 0.9 : 1;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120));
        press.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        press.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    public void Update(Palette palette, double size, double opacity)
    {
        if (!ReferenceEquals(orb.Palette, palette)) orb.Palette = palette;
        if (double.IsNaN(orb.Width) || Math.Abs(orb.Width - size) > 0.1)
        {
            orb.Width = orb.Height = size;
            root.Width = root.Height = size + 16;
        }
        orb.Opacity = opacity;
        var glow = palette.Orb[0];
        if (orb.Effect is not DropShadowEffect d || d.Color != glow)
            orb.Effect = new DropShadowEffect { Color = glow, BlurRadius = size * 0.35, ShadowDepth = 0, Opacity = 0.45 };
    }
}

/// <summary>The recording capsule at the top of the screen (Android <c>CapsuleView</c>, Mac
/// <c>CapsuleContent</c>): orb, live waveform, state, timer, Pause, Stop and Cancel; then "Writing it
/// up…" and "Inserted".</summary>
public sealed class CapsuleWindow : OverlayWindow
{
    public const double W = 440, H = 58;
    readonly DictationController d;
    readonly Orb orb = new() { Width = 40, Height = 40, ForceGlass = true };
    readonly WaveBars wave = new() { Width = 70, Height = 30, Margin = new Thickness(12, 0, 0, 0) };
    readonly TextBlock label = new() { FontSize = 14.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) };
    readonly Ellipse recDot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock time = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 10, 0) };
    readonly Button pause, stop, cancel;
    readonly StackPanel live;
    readonly Border shell = new() { CornerRadius = new CornerRadius(29), Width = W, Height = H, Padding = new Thickness(9, 0, 10, 0) };
    readonly Border ring = new() { CornerRadius = new CornerRadius(29), BorderThickness = new Thickness(1.5), IsHitTestVisible = false };
    readonly RotateTransform ringTurn = new() { CenterX = 0.5, CenterY = 0.5 };
    Palette palette = Palette.Aurora;

    public CapsuleWindow(DictationController d)
    {
        this.d = d;
        Title = "Fluent recording";
        label.FontFamily = Fonts.Title;
        time.FontFamily = Fonts.Body;
        pause = Round(Glyph.Pause, "Pause", () => d.TogglePause());
        stop = Round(Glyph.Stop, "Stop and insert", () => d.Stop());
        cancel = Round(Glyph.Close, "Cancel", () => d.Cancel(), small: true);
        live = new StackPanel { Orientation = Orientation.Horizontal, Children = { recDot, time, pause, stop, cancel } };
        pause.Margin = stop.Margin = new Thickness(0, 0, 6, 0);

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(orb, Dock.Left);
        DockPanel.SetDock(wave, Dock.Left);
        DockPanel.SetDock(live, Dock.Right);
        row.Children.Add(orb);
        row.Children.Add(wave);
        row.Children.Add(live);
        row.Children.Add(label);
        shell.Child = row;
        shell.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.35 };
        var grid = new Grid { Margin = new Thickness(12), Children = { shell, ring } };
        Content = grid;
        System.Windows.Automation.AutomationProperties.SetName(grid, "Fluent recording");

        d.PropertyChanged += OnChange;
        CompositionTarget.Rendering += (_, _) =>
        {
            if (!IsVisible) return;
            ringTurn.Angle = (ringTurn.Angle + 1.5) % 360;
            if (d.Phase == DictationPhase.Recording)
                recDot.Opacity = DateTime.Now.Millisecond < 500 ? 1 : 0.55;
        };
        Refresh();
    }

    Button Round(string glyph, string help, Action action, bool small = false)
    {
        var b = new Button
        {
            Style = Ui.S("Round"), Content = glyph, Width = small ? 26 : 36, Height = small ? 26 : 36,
            FontSize = small ? 9 : 12.5, ToolTip = help, VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, help);
        b.Click += (_, _) => action();
        return b;
    }

    public void SetPalette(Palette p)
    {
        palette = p;
        orb.Palette = p;
        shell.Background = new SolidColorBrush(p.SurfaceStrong);
        label.Foreground = new SolidColorBrush(p.Ink);
        time.Foreground = new SolidColorBrush(p.Dim);
        wave.Fill = new LinearGradientBrush(new GradientStopCollection { new(p.Accent, 0), new(p.Accent2, 0.5), new(p.Voice, 1) }, 0);
        var sweep = new LinearGradientBrush(new GradientStopCollection
        {
            new(p.Accent, 0), new(Palette.WithAlpha(p.Accent2, 0.25), 0.3), new(p.Voice, 0.55), new(Palette.WithAlpha(p.Accent, 0.25), 0.8), new(p.Accent, 1),
        }, 0) { RelativeTransform = ringTurn };
        ring.BorderBrush = sweep;
        pause.Background = new SolidColorBrush(Palette.WithAlpha(p.Ink, 0.12)); pause.Foreground = new SolidColorBrush(p.Ink);
        stop.Background = new SolidColorBrush(p.Danger); stop.Foreground = Brushes.White;
        cancel.Background = new SolidColorBrush(Palette.WithAlpha(p.Ink, 0.08)); cancel.Foreground = new SolidColorBrush(p.Dim);
        Refresh();
    }

    void OnChange(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DictationController.Levels):
                wave.Levels = d.Levels;
                orb.Level = d.Phase == DictationPhase.Recording ? d.Levels[^1] : 0;
                break;
            case nameof(DictationController.ElapsedText): time.Text = d.ElapsedText; break;
            default: Refresh(); break;
        }
    }

    void Refresh()
    {
        var p = d.Phase;
        var isLive = d.IsLive;
        orb.Speed = p switch { DictationPhase.Recording => 120, DictationPhase.Transcribing => 180, DictationPhase.Paused => 0, _ => 50 };
        orb.Glyph = p switch
        {
            DictationPhase.Recording or DictationPhase.Paused => Glyph.Mic,
            DictationPhase.Done => Glyph.Check,
            DictationPhase.Error => "",
            _ => "",
        };
        orb.Tint = p switch
        {
            DictationPhase.Done => [palette.Success, Palette.WithAlpha(palette.Success, 0.6), palette.Success],
            DictationPhase.Error => [palette.Danger, Palette.WithAlpha(palette.Danger, 0.6), palette.Danger],
            _ => null,
        };
        orb.Opacity = p == DictationPhase.Paused ? 0.6 : 1;
        wave.Visibility = isLive || p == DictationPhase.Transcribing ? Visibility.Visible : Visibility.Collapsed;
        wave.Phase = p;
        label.Text = d.Label;
        label.Foreground = new SolidColorBrush(p == DictationPhase.Error ? palette.Danger : palette.Ink);
        live.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
        recDot.Fill = new SolidColorBrush(p == DictationPhase.Recording ? palette.Danger : palette.Faint);
        if (p != DictationPhase.Recording) recDot.Opacity = 1;
        time.Text = d.ElapsedText;
        pause.Content = p == DictationPhase.Paused ? Glyph.Play : Glyph.Pause;
        pause.ToolTip = p == DictationPhase.Paused ? "Resume" : "Pause";
        System.Windows.Automation.AutomationProperties.SetName(pause, p == DictationPhase.Paused ? "Resume" : "Pause");
        ring.Opacity = p is DictationPhase.Recording or DictationPhase.Transcribing ? 1 : 0.45;
    }
}

/// <summary>Owns the bubble and the capsule and keeps them in step with the focused field and the
/// dictation: the job of Android's <c>BubbleService</c> and the Mac's <c>OverlayController</c>.</summary>
public sealed class OverlayController
{
    readonly AppModel model;
    DictationController D => model.Dictation;
    public BubbleWindow Bubble { get; }
    public CapsuleWindow Capsule { get; }
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    int ticks;
    bool querying;
    bool capsuleShown;
    (double X, double Y) autoOrigin;
    public FocusedField? CurrentField { get; private set; }
    /// <summary>Demo screenshots pin the overlays instead of following a real field.</summary>
    public bool Pinned { get; set; }

    public OverlayController(AppModel model)
    {
        this.model = model;
        Bubble = new BubbleWindow();
        Capsule = new CapsuleWindow(model.Dictation);
        Capsule.SetPalette(model.OverlayPalette);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppModel.OverlayPalette) or nameof(AppModel.Palette)) Capsule.SetPalette(model.OverlayPalette);
        };
        Bubble.OnClick = () => D.Toggle(DictationSource.Bubble, CurrentField);
        Bubble.OnHoldStart = () => D.Start(DictationSource.BubbleHold, CurrentField);
        Bubble.OnHoldEnd = () => D.Stop();
        Bubble.OnDragEnd = (x, y) => model.Store.BubbleOffset = (x - autoOrigin.X, y - autoOrigin.Y);
        Bubble.MenuProvider = BubbleMenu;
        timer.Tick += (_, _) => Tick();
    }

    public void Start() => timer.Start();

    void Tick()
    {
        ticks++;
        if (ticks % 4 == 0) model.RefreshPermissions();
        SyncCapsule();
        if (!Pinned) _ = SyncBubbleAsync();
    }

    async Task SyncBubbleAsync()
    {
        if (D.IsBusy)
        {
            // One control at a time: the capsule owns the session. A press-and-hold turn keeps the bubble
            // (invisible) because it is holding the mouse that ends the turn.
            if (D.Source == DictationSource.BubbleHold && D.IsLive) Bubble.Opacity = 0; else HideBubble();
            return;
        }
        if (Bubble.Dragging || querying) return;
        if (!model.BubbleEnabled || !model.TermsAccepted || model.Snoozed) { HideBubble(); return; }
        querying = true;
        FocusedField? field;
        try { field = await FieldFinder.FrontmostAsync(); }
        finally { querying = false; }
        if (D.IsBusy || Bubble.Dragging) return;
        CurrentField = field;
        var allowed = model.Store.BubbleAllowed(model.TermsAccepted, field?.Kind, field?.Process);
        if (allowed && field is not null) ShowBubble(field); else HideBubble();
    }

    public void ShowBubble(FocusedField? field, (double X, double Y)? at = null)
    {
        Bubble.Update(model.OverlayPalette, model.BubbleSize, model.BubbleOpacity);
        Bubble.Opacity = 1;
        var frame = field?.Frame ?? default;
        var probe = at ?? (frame.MidX, frame.MidY);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)probe.X, (int)probe.Y));
        var wa = screen.WorkingArea;
        var scale = Dpi.ScaleAt((int)probe.X, (int)probe.Y);
        var size = (model.BubbleSize + 16) * scale;
        var work = new BubblePlacement.Rect(wa.X, wa.Y, wa.Width, wa.Height);
        (double X, double Y) origin;
        if (at is { } p) { origin = p; autoOrigin = p; }
        else
        {
            autoOrigin = BubblePlacement.Origin(frame, size, work);
            origin = BubblePlacement.Origin(frame, size, work, model.Store.BubbleOffset);
        }
        Bubble.MoveTo(origin.X, origin.Y);
    }

    public void HideBubble()
    {
        if (Bubble.IsVisible && !Bubble.Dragging) Bubble.Hide();
    }

    ContextMenu BubbleMenu()
    {
        var m = new ContextMenu();
        MenuItem Item(string text, Action a) { var i = new MenuItem { Header = text }; i.Click += (_, _) => a(); return i; }
        m.Items.Add(Item("Dictate", () => D.Toggle(DictationSource.Bubble, CurrentField)));
        m.Items.Add(new Separator());
        m.Items.Add(Item($"Snooze for {model.SnoozeChoice.Label()}", () => { model.Snooze(); HideBubble(); }));
        if (CurrentField is { Process.Length: > 0 } f)
            m.Items.Add(Item($"Never show in {f.AppName}", () => { model.Exclude(f.Process); HideBubble(); }));
        m.Items.Add(Item("Reset bubble position", () => model.Store.BubbleOffset = (0, 0)));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Open Fluent…", () => (Application.Current as App)?.ShowMain(null)));
        return m;
    }

    void SyncCapsule()
    {
        var want = D.Phase != DictationPhase.Idle && D.Source != DictationSource.InApp;
        if (want && !capsuleShown) ShowCapsule();
        if (!want && capsuleShown && !Pinned) HideCapsule();
    }

    public void ShowCapsule()
    {
        Capsule.SetPalette(model.OverlayPalette);
        Native.GetCursorPos(out var cursor);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        var wa = screen.WorkingArea;
        var scale = Dpi.ScaleAt(cursor.X, cursor.Y);
        var (x, y) = BubblePlacement.CapsuleOrigin(new BubblePlacement.Rect(wa.X, wa.Y, wa.Width, wa.Height),
            (CapsuleWindow.W + 24) * scale, (CapsuleWindow.H + 24) * scale);
        Capsule.Opacity = 0;
        Capsule.MoveTo(x, y);
        Capsule.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        capsuleShown = true;
    }

    public void HideCapsule()
    {
        Capsule.BeginAnimation(UIElement.OpacityProperty, null);
        Capsule.Hide();
        capsuleShown = false;
    }

    public bool CapsuleVisible => capsuleShown;
}

/// <summary>Display scaling for the monitor under a point (1.0 = 100 %).</summary>
static class Dpi
{
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.POINT pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

    public static double ScaleAt(int x, int y)
    {
        try
        {
            var mon = MonitorFromPoint(new Native.POINT { X = x, Y = y }, 2);
            return GetDpiForMonitor(mon, 0, out var dx, out _) == 0 ? dx / 96.0 : 1;
        }
        catch { return 1; }
    }
}
