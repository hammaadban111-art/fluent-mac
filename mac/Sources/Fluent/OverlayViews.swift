import SwiftUI

/// The glass orb used by the floating bubble and the capsule: a colour sweep turning inside a
/// glass sphere, faster while listening (Android `OrbPainter`). Overlay windows are not part of a
/// SwiftUI scene, so unlike `DictationOrb` it does not depend on the scene phase.
struct OverlayOrb: View {
    var palette: Palette
    var size: CGFloat
    var speed: Double = 24
    var level: CGFloat = 0
    var symbol: String? = "mic.fill"
    var tint: [Color]? = nil

    var body: some View {
        TimelineView(.animation(minimumInterval: 1 / 30)) { tl in
            let t = tl.date.timeIntervalSinceReferenceDate
            let colors = tint ?? palette.orb
            ZStack {
                Circle()
                    .fill(AngularGradient(colors: colors + [colors.first ?? palette.accent], center: .center,
                                          angle: .degrees((t * speed).truncatingRemainder(dividingBy: 360))))
                    .blur(radius: size * 0.1)
                    .scaleEffect(1 + level * 0.14)
                Circle()
                    .fill(RadialGradient(colors: [.white.opacity(0.32), .clear],
                                         center: UnitPoint(x: 0.32, y: 0.26), startRadius: 0, endRadius: size * 0.5))
                Circle().strokeBorder(.white.opacity(0.28), lineWidth: 1)
                if let symbol {
                    Image(systemName: symbol)
                        .font(.system(size: size * 0.34, weight: .semibold))
                        .foregroundStyle(.white)
                        .shadow(color: .black.opacity(0.3), radius: 3)
                }
            }
            .frame(width: size, height: size)
            .clipShape(Circle())
        }
    }
}

/// The floating bubble next to a text box.
struct BubbleContent: View {
    var palette: Palette
    var size: CGFloat
    var opacity: Double
    var pressed: Bool

    var body: some View {
        OverlayOrb(palette: palette, size: size, speed: 30)
            .shadow(color: (palette.orb.first ?? palette.accent).opacity(0.45), radius: size * 0.16)
            .scaleEffect(pressed ? 0.9 : 1)
            .opacity(opacity)
            .animation(.easeOut(duration: 0.12), value: pressed)
            .frame(width: size + 16, height: size + 16)
            .accessibilityLabel("Fluent: dictate into this field")
    }
}

/// The recording capsule at the top of the screen (Android `CapsuleView`): orb, live waveform,
/// state, timer, Pause, Stop and Cancel; then "Writing it up…" and "Inserted".
struct CapsuleContent: View {
    let dictation: DictationController
    var palette: Palette
    var onPause: () -> Void
    var onStop: () -> Void
    var onCancel: () -> Void

    static let size = CGSize(width: 440, height: 58)

    private var phase: DictationController.Phase { dictation.phase }
    private var live: Bool { phase.isLive }

    var body: some View {
        HStack(spacing: 12) {
            OverlayOrb(palette: palette, size: 40,
                       speed: phase == .recording ? 120 : phase == .transcribing ? 180 : phase == .paused ? 0 : 50,
                       level: phase == .recording ? (dictation.levels.last ?? 0) : 0,
                       symbol: orbSymbol, tint: orbTint)
                .opacity(phase == .paused ? 0.6 : 1)
            if live || phase == .transcribing {
                wave.frame(width: 70, height: 30)
            }
            Text(label)
                .font(.system(size: 14.5, weight: .semibold, design: .rounded))
                .foregroundStyle(isError ? palette.danger : palette.ink)
                .lineLimit(1)
                .truncationMode(.tail)
            Spacer(minLength: 4)
            if live {
                TimelineView(.periodic(from: .now, by: 0.5)) { tl in
                    Circle().fill(phase == .recording ? palette.danger : palette.faint)
                        .frame(width: 7, height: 7)
                        .opacity(phase == .recording ? (Int(tl.date.timeIntervalSinceReferenceDate * 2) % 2 == 0 ? 1 : 0.55) : 1)
                }
                .frame(width: 7, height: 7)
                Text(timeString)
                    .font(.system(size: 13, weight: .medium, design: .rounded).monospacedDigit())
                    .foregroundStyle(palette.dim)
                roundButton(symbol: phase == .paused ? "play.fill" : "pause.fill", fill: palette.ink.opacity(0.12),
                      glyph: palette.ink, help: phase == .paused ? "Resume" : "Pause", action: onPause)
                roundButton(symbol: "stop.fill", fill: palette.danger, glyph: .white, help: "Stop and insert", action: onStop)
                roundButton(symbol: "xmark", fill: palette.ink.opacity(0.08), glyph: palette.dim, help: "Cancel", action: onCancel, small: true)
            }
        }
        .padding(.leading, 9)
        .padding(.trailing, 10)
        .frame(width: Self.size.width, height: Self.size.height)
        .background {
            ZStack {
                Capsule().fill(palette.surfaceStrong)
                Capsule().fill(LinearGradient(colors: [.white.opacity(palette.isLight ? 0.4 : 0.1), .clear],
                                              startPoint: .top, endPoint: .bottom))
                TimelineView(.animation(minimumInterval: 1 / 30)) { tl in
                    let a = tl.date.timeIntervalSinceReferenceDate * 90
                    Capsule().strokeBorder(
                        AngularGradient(colors: [palette.accent, palette.accent2.opacity(0.25), palette.voice,
                                                 palette.accent.opacity(0.25), palette.accent],
                                        center: .center, angle: .degrees(a.truncatingRemainder(dividingBy: 360))),
                        lineWidth: 1.5)
                    .opacity(phase == .recording || phase == .transcribing ? 1 : 0.45)
                }
            }
        }
        .shadow(color: .black.opacity(0.35), radius: 14, y: 6)
        .padding(12)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Fluent recording")
    }

    private var isError: Bool { if case .error = phase { true } else { false } }

    private var label: String {
        switch phase {
        case .recording: "Listening"
        case .paused: "Paused"
        case .transcribing: "Writing it up…"
        case .done(let m): m.isEmpty ? "Inserted" : m
        case .error(let m): m.isEmpty ? "Something went wrong" : m
        case .idle: ""
        }
    }

    private var orbSymbol: String? {
        switch phase {
        case .recording, .paused: "mic.fill"
        case .done: "checkmark"
        case .error: "exclamationmark"
        default: nil
        }
    }

    private var orbTint: [Color]? {
        switch phase {
        case .done: [palette.success, palette.success.opacity(0.6), palette.success]
        case .error: [palette.danger, palette.danger.opacity(0.6), palette.danger]
        default: nil
        }
    }

    private var timeString: String {
        let s = Int(dictation.elapsed)
        return String(format: "%d:%02d", s / 60, s % 60)
    }

    private var wave: some View {
        TimelineView(.animation(minimumInterval: 1 / 30)) { tl in
            let t = tl.date.timeIntervalSinceReferenceDate
            let bars = Array(dictation.levels.suffix(9))
            HStack(alignment: .center, spacing: 3.5) {
                ForEach(0..<bars.count, id: \.self) { i in
                    let v = barHeight(i, t: t, level: bars[i])
                    Capsule()
                        .fill(LinearGradient(colors: [palette.accent, palette.accent2, palette.voice],
                                             startPoint: .leading, endPoint: .trailing))
                        .frame(width: 4, height: max(4, v * 30))
                        .opacity(phase == .paused ? 0.4 : 1)
                }
            }
            .frame(maxHeight: .infinity)
        }
    }

    /// Height (0…1) of waveform bar `i`: the live level with a gentle idle ripple while
    /// listening, flat while paused, a travelling wave while Gemini writes the text up.
    private func barHeight(_ i: Int, t: Double, level: CGFloat) -> CGFloat {
        let offset = Double(i) / 9
        switch phase {
        case .recording:
            let ripple: Double = 0.14 + 0.1 * (sin((t - offset) * 2 * Double.pi) + 1) / 2
            return max(level, CGFloat(ripple))
        case .paused:
            return 0.1
        default:
            let wave: Double = 0.2 + 0.45 * (sin((t * 1.5 - offset) * 2 * Double.pi) + 1) / 2
            return CGFloat(wave)
        }
    }

    private func roundButton(symbol: String, fill: Color, glyph: Color, help: String,
                       action: @escaping () -> Void, small: Bool = false) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: small ? 10 : 13, weight: .bold))
                .foregroundStyle(glyph)
                .frame(width: small ? 26 : 36, height: small ? 26 : 36)
                .background(Circle().fill(fill))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .help(help)
        .accessibilityLabel(help)
    }
}
