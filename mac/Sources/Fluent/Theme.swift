import SwiftUI

/// The Fluent themes, the same values as Android's `FluentPalette` (copied from the iPhone app).
/// The Mac offers all six Android themes: Aurora (default), Porcelain, Obsidian, Ember, Lagoon and Classic.
struct Palette: Identifiable, Equatable {
    let id: String
    let name: String
    let tagline: String
    let isLight: Bool
    let background: Color
    let ambient: [Color]
    let surface: Color
    let surfaceStrong: Color
    let border: Color
    let ink: Color
    let dim: Color
    let faint: Color
    let accent: Color
    let accent2: Color
    let onAccent: Color
    let voice: Color
    let success: Color
    let danger: Color
    /// Colours swept around the dictation orb.
    let orb: [Color]
    /// Paper themes drop the glass: flat sheets, no ambient light, an ink orb circled in pen.
    let paper: Bool

    static let all: [Palette] = [aurora, porcelain, obsidian, ember, lagoon, classic]
    static func byID(_ id: String?) -> Palette { all.first { $0.id == id } ?? aurora }

    static let aurora = Palette(
        id: "aurora", name: "Aurora", tagline: "Violet night", isLight: false,
        background: Color(argb: 0xFF07070B), ambient: [Color(argb: 0xFF5B4BFF), Color(argb: 0xFFB04BFF), Color(argb: 0xFF3A2BFF)],
        surface: Color(argb: 0x12FFFFFF), surfaceStrong: Color(argb: 0xF71C1B26), border: Color(argb: 0x1FFFFFFF),
        ink: Color(argb: 0xFFF5F5F7), dim: Color(argb: 0xA8EBEBF5), faint: Color(argb: 0x80EBEBF5),
        accent: Color(argb: 0xFF8B8CFF), accent2: Color(argb: 0xFFB46CFF), onAccent: Color(argb: 0xFFFFFFFF),
        voice: Color(argb: 0xFFFFB27A), success: Color(argb: 0xFF34D399), danger: Color(argb: 0xFFFF6B5E),
        orb: [Color(argb: 0xFF5E5CFF), Color(argb: 0xFFA45BFF), Color(argb: 0xFFFF7AB6), Color(argb: 0xFFFFB27A)], paper: false
    )

    static let porcelain = Palette(
        id: "porcelain", name: "Porcelain", tagline: "Warm paper", isLight: true,
        background: Color(argb: 0xFFF4F2EC), ambient: [Color(argb: 0xFFB9C1FF), Color(argb: 0xFFFFD2B8), Color(argb: 0xFFD9CCFF)],
        surface: Color(argb: 0xB8FFFFFF), surfaceStrong: Color(argb: 0xFAFFFFFF), border: Color(argb: 0x1A16161A),
        ink: Color(argb: 0xFF16161A), dim: Color(argb: 0xFF55534E), faint: Color(argb: 0xFF6E6B64),
        accent: Color(argb: 0xFF2F3BD1), accent2: Color(argb: 0xFF6A4BE0), onAccent: Color(argb: 0xFFFFFFFF),
        voice: Color(argb: 0xFFD9731F), success: Color(argb: 0xFF1E7A4C), danger: Color(argb: 0xFFB42318),
        orb: [Color(argb: 0xFF2F3BD1), Color(argb: 0xFF6A4BE0), Color(argb: 0xFFF29A4A), Color(argb: 0xFF4D7CFF)], paper: false
    )

    static let obsidian = Palette(
        id: "obsidian", name: "Obsidian", tagline: "True black", isLight: false,
        background: Color(argb: 0xFF000000), ambient: [Color(argb: 0xFF3A3A46), Color(argb: 0xFF26262E), Color(argb: 0xFF4A4A56)],
        surface: Color(argb: 0x10FFFFFF), surfaceStrong: Color(argb: 0xF7141416), border: Color(argb: 0x1FFFFFFF),
        ink: Color(argb: 0xFFFFFFFF), dim: Color(argb: 0xA6FFFFFF), faint: Color(argb: 0x80FFFFFF),
        accent: Color(argb: 0xFFE8E8ED), accent2: Color(argb: 0xFF9A9AA6), onAccent: Color(argb: 0xFF000000),
        voice: Color(argb: 0xFFFFFFFF), success: Color(argb: 0xFF5EE0A8), danger: Color(argb: 0xFFFF6B5E),
        orb: [Color(argb: 0xFF2E2E36), Color(argb: 0xFFE8E8ED), Color(argb: 0xFF9AA4C8), Color(argb: 0xFFFFFFFF), Color(argb: 0xFFD8C8E8)], paper: false
    )

    static let ember = Palette(
        id: "ember", name: "Ember", tagline: "Coral glow", isLight: false,
        background: Color(argb: 0xFF0C0706), ambient: [Color(argb: 0xFFFF6A3D), Color(argb: 0xFFE0457B), Color(argb: 0xFF8A3B1F)],
        surface: Color(argb: 0x12FFFFFF), surfaceStrong: Color(argb: 0xF7211614), border: Color(argb: 0x1FFFFFFF),
        ink: Color(argb: 0xFFFFF6F2), dim: Color(argb: 0xA8FFEDE6), faint: Color(argb: 0x80FFEDE6),
        accent: Color(argb: 0xFFFF8A5B), accent2: Color(argb: 0xFFFF4F8B), onAccent: Color(argb: 0xFFFFFFFF),
        voice: Color(argb: 0xFFFFD27A), success: Color(argb: 0xFF5EE0A8), danger: Color(argb: 0xFFFF5A5A),
        orb: [Color(argb: 0xFFFF6A3D), Color(argb: 0xFFFF4F8B), Color(argb: 0xFFFFB35C), Color(argb: 0xFFFF8A5B)], paper: false
    )

    static let lagoon = Palette(
        id: "lagoon", name: "Lagoon", tagline: "Deep teal", isLight: false,
        background: Color(argb: 0xFF040B0C), ambient: [Color(argb: 0xFF0FB5A6), Color(argb: 0xFF2B6BFF), Color(argb: 0xFF0B7F74)],
        surface: Color(argb: 0x12FFFFFF), surfaceStrong: Color(argb: 0xF7101C1E), border: Color(argb: 0x1FFFFFFF),
        ink: Color(argb: 0xFFF2FFFD), dim: Color(argb: 0xA8E6FFFB), faint: Color(argb: 0x80E6FFFB),
        accent: Color(argb: 0xFF3EE6C8), accent2: Color(argb: 0xFF3B9BFF), onAccent: Color(argb: 0xFF03201B),
        voice: Color(argb: 0xFF9CF6E5), success: Color(argb: 0xFF5EE0A8), danger: Color(argb: 0xFFFF6B5E),
        orb: [Color(argb: 0xFF0FB5A6), Color(argb: 0xFF3B9BFF), Color(argb: 0xFF7CF2DA), Color(argb: 0xFF1E7BFF)], paper: false
    )

    /// Paper, ink and a blue proofreading pen: the look of the Fluent website.
    static let classic = Palette(
        id: "classic", name: "Classic", tagline: "Paper and ink", isLight: true,
        background: Color(argb: 0xFFF4F2EC), ambient: [Color(argb: 0xFFF4F2EC), Color(argb: 0xFFF4F2EC), Color(argb: 0xFFF4F2EC)],
        surface: Color(argb: 0xFFFFFDF8), surfaceStrong: Color(argb: 0xFFFFFDF8), border: Color(argb: 0xFFDCD8CD),
        ink: Color(argb: 0xFF16161A), dim: Color(argb: 0xFF4A4843), faint: Color(argb: 0xFF6E6B64),
        accent: Color(argb: 0xFF2F3BD1), accent2: Color(argb: 0xFF2F3BD1), onAccent: Color(argb: 0xFFFFFFFF),
        voice: Color(argb: 0xFFC2621A), success: Color(argb: 0xFF1E7A4C), danger: Color(argb: 0xFFB42318),
        orb: [Color(argb: 0xFF16161A), Color(argb: 0xFF2F3BD1), Color(argb: 0xFFC2621A), Color(argb: 0xFF16161A)], paper: true
    )

}

extension Color {
    /// 0xAARRGGBB, as Android writes colours.
    init(argb: UInt32) {
        self.init(.sRGB,
                  red: Double((argb >> 16) & 0xFF) / 255,
                  green: Double((argb >> 8) & 0xFF) / 255,
                  blue: Double(argb & 0xFF) / 255,
                  opacity: Double((argb >> 24) & 0xFF) / 255)
    }
}

private struct PaletteKey: EnvironmentKey {
    static let defaultValue: Palette = .aurora
}

extension EnvironmentValues {
    var palette: Palette {
        get { self[PaletteKey.self] }
        set { self[PaletteKey.self] = newValue }
    }
}

enum FluentFont {
    static func title(_ size: CGFloat) -> Font { .system(size: size, weight: .semibold, design: .rounded) }
    static func body(_ size: CGFloat = 16) -> Font { .system(size: size, weight: .regular, design: .rounded) }
    static func mono(_ size: CGFloat = 12) -> Font { .system(size: size, weight: .medium, design: .monospaced) }
    /// Classic's handwritten pen notes.
    static func pen(_ size: CGFloat = 17) -> Font { .custom("Noteworthy-Bold", size: size) }
}
