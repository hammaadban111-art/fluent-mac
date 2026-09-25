import SwiftUI

/// The Fluent mark: a lowercase f whose crossbar is a voice waveform. Same geometry as Android's
/// `FluentMark` (a 120-unit box).
struct FluentMark: View {
    var colors: [Color]? = nil
    var bars: [CGFloat] = [1, 1, 1, 1]
    @Environment(\.palette) private var p

    var body: some View {
        Canvas { ctx, size in
            let u = min(size.width, size.height) / 120
            let fill = colors ?? (p.paper ? [p.ink, p.ink] : [p.accent, p.accent2, p.voice])
            let shading = GraphicsContext.Shading.linearGradient(
                Gradient(colors: fill), startPoint: .zero, endPoint: CGPoint(x: size.width, y: size.height))

            var stem = Path()
            stem.move(to: CGPoint(x: 50 * u, y: 94 * u))
            stem.addLine(to: CGPoint(x: 50 * u, y: 50 * u))
            stem.addCurve(to: CGPoint(x: 72 * u, y: 28 * u),
                          control1: CGPoint(x: 50 * u, y: 36 * u), control2: CGPoint(x: 58 * u, y: 28 * u))
            ctx.stroke(stem, with: shading, style: StrokeStyle(lineWidth: 11 * u, lineCap: .round))

            let specs: [(CGFloat, CGFloat)] = [(36, 6), (64, 14), (78, 8), (92, 4)]
            for (i, spec) in specs.enumerated() {
                let h = spec.1 * min(max(bars.indices.contains(i) ? bars[i] : 1, 0), 1.6)
                guard h > 0 else { continue }
                var bar = Path()
                bar.move(to: CGPoint(x: spec.0 * u, y: (58 - h) * u))
                bar.addLine(to: CGPoint(x: spec.0 * u, y: (58 + h) * u))
                ctx.stroke(bar, with: shading, style: StrokeStyle(lineWidth: 9 * u, lineCap: .round))
            }
        }
        .accessibilityHidden(true)
    }
}

/// Classic's hand-drawn circle: a pen loop that overshoots and drifts outward, like a real pen.
struct PenLoop: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        let cx = rect.midX, cy = rect.midY
        let r = min(rect.width, rect.height) / 2
        let steps = 96
        let sweep = 2 * Double.pi * 1.08
        for i in 0...steps {
            let t = Double(i) / Double(steps)
            let a = -Double.pi * 0.62 + sweep * t
            let rr = Double(r) * (0.965 + 0.02 * sin(a * 3 + 0.7) + 0.03 * t)
            let pt = CGPoint(x: cx + CGFloat(rr * cos(a)), y: cy + CGFloat(rr * sin(a) * 0.97))
            if i == 0 { path.move(to: pt) } else { path.addLine(to: pt) }
        }
        return path
    }
}

/// The dictation orb. Glass themes: a blurred colour sweep turning inside a glass sphere, faster
/// while listening. Classic: an ink disc circled in pen.
struct DictationOrb: View {
    var size: CGFloat
    var active: Bool
    var level: CGFloat = 0
    var symbol: String = "mic.fill"
    @Environment(\.palette) private var p
    /// The window is an NSHostingView outside any SwiftUI scene, so there is no scene phase to
    /// pause on; AppKit stops drawing hidden windows anyway.
    private let phase = ScenePhase.active

    var body: some View {
        if p.paper { paper } else { glass }
    }

    private var paper: some View {
        ZStack {
            PenLoop()
                .stroke(p.accent, style: StrokeStyle(lineWidth: max(1.2, min(3, size / 26)), lineCap: .round))
                .frame(width: size, height: size)
                .scaleEffect(1 + level * 0.08)
            Circle().fill(p.ink).frame(width: size * 0.72, height: size * 0.72)
            Image(systemName: symbol)
                .font(.system(size: size * 0.26, weight: .semibold))
                .foregroundStyle(.white)
        }
        .animation(.easeOut(duration: 0.12), value: level)
    }

    private var glass: some View {
        TimelineView(.animation(paused: phase != .active)) { timeline in
            let t = timeline.date.timeIntervalSinceReferenceDate
            let turn = Angle.degrees((t * (active ? 90 : 24)).truncatingRemainder(dividingBy: 360))
            ZStack {
                Circle()
                    .fill(AngularGradient(colors: p.orb + [p.orb.first ?? p.accent], center: .center, angle: turn))
                    .blur(radius: size * 0.12)
                    .opacity(active ? 1 : 0.75)
                    .scaleEffect(1 + level * 0.12)
                Circle()
                    .fill(RadialGradient(colors: [.white.opacity(0.28), .clear],
                                         center: UnitPoint(x: 0.32, y: 0.26), startRadius: 0, endRadius: size * 0.5))
                Circle().strokeBorder(.white.opacity(0.22), lineWidth: 1)
                Image(systemName: symbol)
                    .font(.system(size: size * 0.24, weight: .semibold))
                    .foregroundStyle(.white)
                    .shadow(color: .black.opacity(0.25), radius: 6)
            }
            .frame(width: size, height: size)
            .clipShape(Circle())
            .shadow(color: (p.orb.first ?? p.accent).opacity(active ? 0.55 : 0.3), radius: size * 0.18)
        }
        .animation(.easeOut(duration: 0.12), value: level)
    }
}

/// Live bars driven by the microphone level.
struct Waveform: View {
    var level: CGFloat
    var bars = 24
    var color: Color
    @State private var history: [CGFloat] = []

    var body: some View {
        GeometryReader { geo in
            HStack(alignment: .center, spacing: geo.size.width / CGFloat(bars) * 0.35) {
                ForEach(0..<bars, id: \.self) { i in
                    let v = i < history.count ? history[i] : 0
                    Capsule().fill(color)
                        .frame(height: max(3, geo.size.height * v))
                }
            }
            .frame(maxHeight: .infinity)
        }
        .onChange(of: level) { _, new in
            history.append(min(1, max(0.06, new)))
            if history.count > bars { history.removeFirst(history.count - bars) }
        }
    }
}

/// Soft drifting light behind glass themes; nothing for paper.
struct AmbientBackground: View {
    @Environment(\.palette) private var p
    /// The window is an NSHostingView outside any SwiftUI scene, so there is no scene phase to
    /// pause on; AppKit stops drawing hidden windows anyway.
    private let phase = ScenePhase.active

    var body: some View {
        ZStack {
            p.background
            if !p.paper {
                TimelineView(.animation(minimumInterval: 1 / 30, paused: phase != .active)) { tl in
                    let t = tl.date.timeIntervalSinceReferenceDate / 9
                    // Radial gradients rather than a blur filter: the same soft light for a
                    // fraction of the GPU work, which matters for a screen that animates.
                    Canvas { ctx, size in
                        for (i, c) in p.ambient.enumerated() {
                            let a = t + Double(i) * 2.1
                            let center = CGPoint(x: size.width * (0.5 + 0.35 * cos(a)),
                                                 y: size.height * (0.35 + 0.25 * sin(a * 0.8)))
                            let r = max(size.width, size.height) * 0.42
                            let glow = c.opacity(p.isLight ? 0.35 : 0.30)
                            ctx.fill(Path(ellipseIn: CGRect(x: center.x - r, y: center.y - r, width: r * 2, height: r * 2)),
                                     with: .radialGradient(Gradient(colors: [glow, glow.opacity(0)]),
                                                           center: center, startRadius: 0, endRadius: r))
                        }
                    }
                }
            }
        }
        .ignoresSafeArea()
    }
}

/// A grouped surface: frosted glass for glass themes, a flat ruled sheet for paper.
struct Card<Content: View>: View {
    var padding: CGFloat = 16
    @ViewBuilder var content: Content
    @Environment(\.palette) private var p

    var body: some View {
        content
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background {
                RoundedRectangle(cornerRadius: p.paper ? 6 : 22, style: .continuous)
                    .fill(p.paper ? AnyShapeStyle(p.surface) : AnyShapeStyle(.ultraThinMaterial))
                    .overlay(RoundedRectangle(cornerRadius: p.paper ? 6 : 22, style: .continuous).fill(p.surface))
                    .overlay(RoundedRectangle(cornerRadius: p.paper ? 6 : 22, style: .continuous)
                        .strokeBorder(p.border, lineWidth: 1))
            }
    }
}

struct PrimaryButtonStyle: ButtonStyle {
    @Environment(\.palette) private var p
    @Environment(\.isEnabled) private var enabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(FluentFont.title(17))
            .frame(maxWidth: .infinity)
            .padding(.vertical, 15)
            .foregroundStyle(p.paper ? .white : p.onAccent)
            .background {
                RoundedRectangle(cornerRadius: p.paper ? 6 : 16, style: .continuous)
                    .fill(p.paper ? AnyShapeStyle(p.ink)
                          : AnyShapeStyle(LinearGradient(colors: [p.accent, p.accent2], startPoint: .leading, endPoint: .trailing)))
            }
            .opacity(enabled ? (configuration.isPressed ? 0.8 : 1) : 0.4)
            .scaleEffect(configuration.isPressed ? 0.98 : 1)
    }
}

struct SectionLabel: View {
    var text: String
    @Environment(\.palette) private var p
    var body: some View {
        Text(text.uppercased())
            .font(FluentFont.mono(11))
            .tracking(1.2)
            .foregroundStyle(p.faint)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.leading, 4)
    }
}
