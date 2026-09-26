using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Fluent;

/// <summary>The Fluent themes, the same values as Android's <c>FluentPalette</c> and the Mac's <c>Palette</c>.
/// Windows offers all six: Aurora (default), Porcelain, Obsidian, Ember, Lagoon and Classic.</summary>
public sealed record Palette(
    string Id, string Name, string Tagline, bool IsLight,
    Color Background, Color[] Ambient, Color Surface, Color SurfaceStrong, Color Border,
    Color Ink, Color Dim, Color Faint, Color Accent, Color Accent2, Color OnAccent,
    Color Voice, Color Success, Color Danger, Color[] Orb, bool Paper)
{
    static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static readonly Palette Aurora = new("aurora", "Aurora", "Violet night", false,
        C(0xFF07070B), [C(0xFF5B4BFF), C(0xFFB04BFF), C(0xFF3A2BFF)],
        C(0x12FFFFFF), C(0xF71C1B26), C(0x1FFFFFFF),
        C(0xFFF5F5F7), C(0xA8EBEBF5), C(0x80EBEBF5),
        C(0xFF8B8CFF), C(0xFFB46CFF), C(0xFFFFFFFF),
        C(0xFFFFB27A), C(0xFF34D399), C(0xFFFF6B5E),
        [C(0xFF5E5CFF), C(0xFFA45BFF), C(0xFFFF7AB6), C(0xFFFFB27A)], false);

    public static readonly Palette Porcelain = new("porcelain", "Porcelain", "Warm paper", true,
        C(0xFFF4F2EC), [C(0xFFB9C1FF), C(0xFFFFD2B8), C(0xFFD9CCFF)],
        C(0xB8FFFFFF), C(0xFAFFFFFF), C(0x1A16161A),
        C(0xFF16161A), C(0xFF55534E), C(0xFF6E6B64),
        C(0xFF2F3BD1), C(0xFF6A4BE0), C(0xFFFFFFFF),
        C(0xFFD9731F), C(0xFF1E7A4C), C(0xFFB42318),
        [C(0xFF2F3BD1), C(0xFF6A4BE0), C(0xFFF29A4A), C(0xFF4D7CFF)], false);

    public static readonly Palette Obsidian = new("obsidian", "Obsidian", "True black", false,
        C(0xFF000000), [C(0xFF3A3A46), C(0xFF26262E), C(0xFF4A4A56)],
        C(0x10FFFFFF), C(0xF7141416), C(0x1FFFFFFF),
        C(0xFFFFFFFF), C(0xA6FFFFFF), C(0x80FFFFFF),
        C(0xFFE8E8ED), C(0xFF9A9AA6), C(0xFF000000),
        C(0xFFFFFFFF), C(0xFF5EE0A8), C(0xFFFF6B5E),
        [C(0xFF2E2E36), C(0xFFE8E8ED), C(0xFF9AA4C8), C(0xFFFFFFFF), C(0xFFD8C8E8)], false);

    public static readonly Palette Ember = new("ember", "Ember", "Coral glow", false,
        C(0xFF0C0706), [C(0xFFFF6A3D), C(0xFFE0457B), C(0xFF8A3B1F)],
        C(0x12FFFFFF), C(0xF7211614), C(0x1FFFFFFF),
        C(0xFFFFF6F2), C(0xA8FFEDE6), C(0x80FFEDE6),
        C(0xFFFF8A5B), C(0xFFFF4F8B), C(0xFFFFFFFF),
        C(0xFFFFD27A), C(0xFF5EE0A8), C(0xFFFF5A5A),
        [C(0xFFFF6A3D), C(0xFFFF4F8B), C(0xFFFFB35C), C(0xFFFF8A5B)], false);

    public static readonly Palette Lagoon = new("lagoon", "Lagoon", "Deep teal", false,
        C(0xFF040B0C), [C(0xFF0FB5A6), C(0xFF2B6BFF), C(0xFF0B7F74)],
        C(0x12FFFFFF), C(0xF7101C1E), C(0x1FFFFFFF),
        C(0xFFF2FFFD), C(0xA8E6FFFB), C(0x80E6FFFB),
        C(0xFF3EE6C8), C(0xFF3B9BFF), C(0xFF03201B),
        C(0xFF9CF6E5), C(0xFF5EE0A8), C(0xFFFF6B5E),
        [C(0xFF0FB5A6), C(0xFF3B9BFF), C(0xFF7CF2DA), C(0xFF1E7BFF)], false);

    /// <summary>Paper, ink and a blue proofreading pen: the look of the Fluent website.</summary>
    public static readonly Palette Classic = new("classic", "Classic", "Paper and ink", true,
        C(0xFFF4F2EC), [C(0xFFF4F2EC), C(0xFFF4F2EC), C(0xFFF4F2EC)],
        C(0xFFFFFDF8), C(0xFFFFFDF8), C(0xFFDCD8CD),
        C(0xFF16161A), C(0xFF4A4843), C(0xFF6E6B64),
        C(0xFF2F3BD1), C(0xFF2F3BD1), C(0xFFFFFFFF),
        C(0xFFC2621A), C(0xFF1E7A4C), C(0xFFB42318),
        [C(0xFF16161A), C(0xFF2F3BD1), C(0xFFC2621A), C(0xFF16161A)], true);

    public static readonly IReadOnlyList<Palette> All = [Aurora, Porcelain, Obsidian, Ember, Lagoon, Classic];

    public static Palette ById(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Aurora;

    public static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)(a * 255), c.R, c.G, c.B);

    /// <summary>Flat colour for a see-through surface over the background, for places that cannot blend.</summary>
    public Color Flatten(Color over)
    {
        var a = over.A / 255.0;
        byte Mix(byte f, byte b) => (byte)(f * a + b * (1 - a));
        return Color.FromRgb(Mix(over.R, Background.R), Mix(over.G, Background.G), Mix(over.B, Background.B));
    }
}

/// <summary>Puts a palette into the application resources. Every view uses DynamicResource, so a new
/// theme repaints the whole app at once.</summary>
public static class ThemeResources
{
    static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public static void Apply(ResourceDictionary r, Palette p)
    {
        r["FBg"] = B(p.Background);
        r["FBgColor"] = p.Background;
        r["FSurface"] = B(p.Surface);
        r["FSurfaceStrong"] = B(p.SurfaceStrong);
        r["FSurfaceFlat"] = B(p.Flatten(p.SurfaceStrong));
        r["FBorder"] = B(p.Paper ? p.Border : Palette.WithAlpha(p.Border, System.Math.Min(1, p.Border.A / 255.0 * 1.6)));
        r["FInk"] = B(p.Ink);
        r["FDim"] = B(p.Dim);
        r["FFaint"] = B(p.Faint);
        r["FAccent"] = B(p.Accent);
        r["FAccentColor"] = p.Accent;
        r["FAccent2"] = B(p.Accent2);
        r["FOnAccent"] = B(p.Paper ? Colors.White : p.OnAccent);
        r["FVoice"] = B(p.Voice);
        r["FSuccess"] = B(p.Success);
        r["FDanger"] = B(p.Danger);
        r["FAccentSoft"] = B(Palette.WithAlpha(p.Accent, 0.16));
        r["FSuccessSoft"] = B(Palette.WithAlpha(p.Success, 0.18));
        r["FDangerSoft"] = B(Palette.WithAlpha(p.Danger, 0.14));
        r["FInkSoft"] = B(Palette.WithAlpha(p.Ink, 0.08));
        r["FInkSofter"] = B(Palette.WithAlpha(p.Ink, 0.05));
        r["FSidebar"] = B(Palette.WithAlpha(p.Surface, p.Surface.A / 255.0 * 0.5));
        var primary = p.Paper
            ? (Brush)B(p.Ink)
            : new LinearGradientBrush(p.Accent, p.Accent2, new Point(0, 0.5), new Point(1, 0.5));
        primary.Freeze();
        r["FPrimaryFill"] = primary;
        var mark = p.Paper
            ? (Brush)B(p.Ink)
            : new LinearGradientBrush(new GradientStopCollection
            {
                new(p.Accent, 0), new(p.Accent2, 0.5), new(p.Voice, 1),
            }, new Point(0, 0), new Point(1, 1));
        mark.Freeze();
        r["FMarkFill"] = mark;
        r["FCardRadius"] = new CornerRadius(p.Paper ? 6 : 20);
        r["FButtonRadius"] = new CornerRadius(p.Paper ? 6 : 14);
        r["FControlRadius"] = new CornerRadius(p.Paper ? 4 : 10);
    }
}

public static class Fonts
{
    public static readonly FontFamily Title = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Body = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
}

/// <summary>Icon glyphs from Segoe Fluent Icons (Windows 11), which Segoe MDL2 Assets (Windows 10) shares.</summary>
public static class Glyph
{
    public const string Mic = "", Stop = "", Pause = "", Play = "", Close = "";
    public const string Check = "", Clock = "", Settings = "", Font = "", Copy = "";
    public const string Delete = "", Warning = "", Error = "", Keyboard = "", Touch = "";
    public const string Info = "", Search = "", Link = "", Moon = "", Add = "";
    public const string Exclaim = "", Key = "", Home = "", Shield = "";
}
