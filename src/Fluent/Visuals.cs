using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Fluent;

/// <summary>The Fluent mark: an f whose crossbar is a voice waveform. Same geometry as Android's and the
/// Mac's <c>FluentMark</c> and website-v2/favicon.svg (a 120-unit box), in the theme's colours.</summary>
public sealed class FluentMark : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(FluentMark),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var u = Math.Min(ActualWidth, ActualHeight) / 120;
        if (u <= 0) return;
        var ox = (ActualWidth - 120 * u) / 2;
        var oy = (ActualHeight - 120 * u) / 2;
        Point P(double x, double y) => new(ox + x * u, oy + y * u);
        var stemPen = new Pen(Fill, 11 * u) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var stem = new StreamGeometry();
        using (var g = stem.Open())
        {
            g.BeginFigure(P(50, 94), false, false);
            g.LineTo(P(50, 50), true, false);
            g.BezierTo(P(50, 36), P(58, 28), P(72, 28), true, false);
        }
        dc.DrawGeometry(null, stemPen, stem);
        var barPen = new Pen(Fill, 9 * u) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var (x, h) in new[] { (36.0, 6.0), (64, 14), (78, 8), (92, 4) })
            dc.DrawLine(barPen, P(x, 58 - h), P(x, 58 + h));
    }
}

/// <summary>The dictation orb (Android <c>OrbPainter</c>): a blurred colour sweep turning inside a glass
/// sphere, faster while listening. Paper themes: an ink disc circled in pen.</summary>
public sealed class Orb : Grid
{
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(nameof(Palette), typeof(Palette), typeof(Orb),
        new PropertyMetadata(Fluent.Palette.Aurora, (d, _) => ((Orb)d).Rebuild()));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(Orb),
        new PropertyMetadata(Fluent.Glyph.Mic, (d, e) => ((Orb)d).glyph.Text = (string)e.NewValue ?? ""));
    public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(nameof(Speed), typeof(double), typeof(Orb), new PropertyMetadata(24.0));
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(Orb),
        new PropertyMetadata(0.0, (d, e) => ((Orb)d).ApplyLevel()));
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Color[]), typeof(Orb),
        new PropertyMetadata(null, (d, _) => ((Orb)d).Rebuild()));
    public static readonly DependencyProperty ForceGlassProperty = DependencyProperty.Register(nameof(ForceGlass), typeof(bool), typeof(Orb),
        new PropertyMetadata(false, (d, _) => ((Orb)d).Rebuild()));

    public Palette Palette { get => (Palette)GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    /// <summary>Degrees per second.</summary>
    public double Speed { get => (double)GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public Color[]? Tint { get => (Color[]?)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    /// <summary>The bubble and capsule always use the glass orb, as on the Mac.</summary>
    public bool ForceGlass { get => (bool)GetValue(ForceGlassProperty); set => SetValue(ForceGlassProperty, value); }

    readonly TextBlock glyph = new()
    {
        FontFamily = Fonts.Icons, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, Text = Fluent.Glyph.Mic,
        Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.35, Color = Colors.Black },
    };
    readonly RotateTransform turn = new();
    readonly ScaleTransform pulse = new(1, 1);
    double angle;
    TimeSpan last;

    public Orb()
    {
        Loaded += (_, _) => { CompositionTarget.Rendering += OnFrame; Rebuild(); };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
        SizeChanged += (_, _) => Rebuild();
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = pulse;
    }

    bool Paper => Palette.Paper && !ForceGlass;

    void Rebuild()
    {
        Children.Clear();
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || double.IsNaN(size)) return;
        glyph.FontSize = size * (Paper ? 0.26 : 0.3);
        if (Paper)
        {
            Children.Add(new Path
            {
                Data = PenLoop(size), Stroke = new SolidColorBrush(Palette.Accent),
                StrokeThickness = Math.Clamp(size / 26, 1.2, 3), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
            Children.Add(new Ellipse { Width = size * 0.72, Height = size * 0.72, Fill = new SolidColorBrush(Palette.Ink) });
            Children.Add(glyph);
            Clip = null;
            return;
        }
        var colors = Tint ?? Palette.Orb;
        var sweep = new Ellipse
        {
            Fill = new ImageBrush(Conic(colors)) { Stretch = Stretch.Fill },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = turn,
            Effect = new BlurEffect { Radius = size * 0.12, KernelType = KernelType.Gaussian },
            Margin = new Thickness(-size * 0.08),
        };
        Children.Add(sweep);
        Children.Add(new Ellipse
        {
            Fill = new RadialGradientBrush(Color.FromArgb(82, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))
            { GradientOrigin = new Point(0.32, 0.26), Center = new Point(0.32, 0.26), RadiusX = 0.55, RadiusY = 0.55 },
        });
        Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = 1 });
        Children.Add(glyph);
        Clip = new EllipseGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
    }

    void ApplyLevel()
    {
        var s = 1 + Math.Clamp(Level, 0, 1) * (Paper ? 0.06 : 0.1);
        pulse.ScaleX = pulse.ScaleY = s;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        if (Paper || !IsVisible) return;
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = last == TimeSpan.Zero ? 0 : (now - last).TotalSeconds;
        last = now;
        if (dt <= 0 || dt > 0.5) return;
        angle = (angle + Speed * dt) % 360;
        turn.Angle = angle;
    }

    static readonly Dictionary<string, BitmapSource> ConicCache = new();

    /// <summary>WPF has no angular gradient, so the colour sweep is drawn once into a small bitmap.</summary>
    public static BitmapSource Conic(Color[] colors)
    {
        var key = string.Join(",", colors.Select(c => c.ToString()));
        if (ConicCache.TryGetValue(key, out var hit)) return hit;
        const int n = 128;
        var stops = colors.Append(colors[0]).ToArray();
        var px = new byte[n * n * 4];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var a = (Math.Atan2(y - n / 2.0 + 0.5, x - n / 2.0 + 0.5) / (2 * Math.PI) + 1) % 1;
                var f = a * (stops.Length - 1);
                var i = (int)Math.Floor(f);
                var t = f - i;
                var c0 = stops[i]; var c1 = stops[Math.Min(i + 1, stops.Length - 1)];
                var o = (y * n + x) * 4;
                px[o] = (byte)(c0.B + (c1.B - c0.B) * t);
                px[o + 1] = (byte)(c0.G + (c1.G - c0.G) * t);
                px[o + 2] = (byte)(c0.R + (c1.R - c0.R) * t);
                px[o + 3] = 255;
            }
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
        bmp.Freeze();
        ConicCache[key] = bmp;
        return bmp;
    }

    /// <summary>Classic's hand-drawn circle: a pen loop that overshoots and drifts outward, like a real pen.</summary>
    static Geometry PenLoop(double size)
    {
        var g = new StreamGeometry();
        var r = size / 2;
        using (var ctx = g.Open())
        {
            const int steps = 96;
            var sweep = 2 * Math.PI * 1.08;
            for (var i = 0; i <= steps; i++)
            {
                var t = (double)i / steps;
                var a = -Math.PI * 0.62 + sweep * t;
                var rr = r * (0.965 + 0.02 * Math.Sin(a * 3 + 0.7) + 0.03 * t);
                var p = new Point(r + rr * Math.Cos(a), r + rr * Math.Sin(a) * 0.97);
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
            }
        }
        g.Freeze();
        return g;
    }
}

/// <summary>Soft drifting light behind glass themes; nothing for paper.</summary>
public sealed class AmbientBackground : FrameworkElement
{
    Palette palette = Palette.Aurora;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    TimeSpan lastFrame;

    public AmbientBackground()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnFrame;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
        IsHitTestVisible = false;
        ClipToBounds = true;   // the glows reach past the window edge
    }

    public void SetPalette(Palette p) { palette = p; InvalidateVisual(); }

    /// <summary>Frozen for screenshots so every capture of a screen looks the same.</summary>
    public static double? FixedTime { get; set; }

    void OnFrame(object? s, EventArgs e)
    {
        if (palette.Paper || !IsVisible) return;
        var now = clock.Elapsed;
        if (now - lastFrame < TimeSpan.FromMilliseconds(33)) return;   // 30 fps is plenty for a slow drift
        lastFrame = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        dc.DrawRectangle(new SolidColorBrush(palette.Background), null, new Rect(0, 0, w, h));
        if (palette.Paper) return;
        var t = (FixedTime ?? clock.Elapsed.TotalSeconds) / 9;
        for (var i = 0; i < palette.Ambient.Length; i++)
        {
            var a = t + i * 2.1;
            var c = new Point(w * (0.5 + 0.35 * Math.Cos(a)), h * (0.35 + 0.25 * Math.Sin(a * 0.8)));
            var r = Math.Max(w, h) * 0.42;
            var glow = Palette.WithAlpha(palette.Ambient[i], palette.IsLight ? 0.35 : 0.30);
            var brush = new RadialGradientBrush(glow, Palette.WithAlpha(palette.Ambient[i], 0))
            {
                MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = r, RadiusY = r,
            };
            dc.DrawEllipse(brush, null, c, r, r);
        }
    }
}

/// <summary>The capsule's live bars (Mac <c>CapsuleContent.wave</c>).</summary>
public sealed class WaveBars : FrameworkElement
{
    public double[] Levels { get; set; } = new double[9];
    public DictationPhase Phase { get; set; }
    public Brush Fill { get; set; } = Brushes.White;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    public WaveBars()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += Frame;
        Unloaded += (_, _) => CompositionTarget.Rendering -= Frame;
    }

    void Frame(object? s, EventArgs e) { if (IsVisible) InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        const int bars = 9;
        var t = clock.Elapsed.TotalSeconds;
        var levels = Levels.Length >= bars ? Levels[^bars..] : Levels;
        var gap = 3.5; var bw = 4.0;
        var total = bars * bw + (bars - 1) * gap;
        var x0 = (ActualWidth - total) / 2;
        for (var i = 0; i < bars; i++)
        {
            var offset = i / 9.0;
            var level = i < levels.Length ? levels[i] : 0;
            double v = Phase switch
            {
                DictationPhase.Recording => Math.Max(level, 0.14 + 0.1 * (Math.Sin((t - offset) * 2 * Math.PI) + 1) / 2),
                DictationPhase.Paused => 0.1,
                _ => 0.2 + 0.45 * (Math.Sin((t * 1.5 - offset) * 2 * Math.PI) + 1) / 2,
            };
            var hgt = Math.Max(4, v * ActualHeight);
            var rect = new Rect(x0 + i * (bw + gap), (ActualHeight - hgt) / 2, bw, hgt);
            dc.PushOpacity(Phase == DictationPhase.Paused ? 0.4 : 1);
            dc.DrawRoundedRectangle(Fill, null, rect, bw / 2, bw / 2);
            dc.Pop();
        }
    }
}
