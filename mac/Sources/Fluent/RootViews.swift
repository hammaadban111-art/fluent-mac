import AppKit
import FluentCore
import FluentMacKit
import SwiftUI

struct RootView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        ZStack {
            AmbientBackground()
            if !model.termsAccepted {
                TermsView().transition(.opacity)
            } else if !model.setupComplete {
                OnboardingView().transition(.opacity)
            } else {
                MainView().transition(.opacity)
            }
        }
        .environment(\.palette, model.palette)
        .preferredColorScheme(model.palette.isLight ? .light : .dark)
        .tint(model.palette.accent)
        .frame(minWidth: 860, minHeight: 620)
        .animation(.easeInOut(duration: 0.25), value: model.termsAccepted)
        .animation(.easeInOut(duration: 0.25), value: model.setupComplete)
    }
}

/// Clickwrap gate, as on Android 1.8.1: one checkbox that links the Terms and the Privacy Policy.
/// Shown before setup, and again whenever `Constants.termsVersion` changes. Nothing works,
/// including the bubble and the shortcuts, until it is accepted.
struct TermsView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.palette) private var p
    @State private var agreed = false
    @State private var appeared = false

    private var returning: Bool { model.setupComplete }

    var body: some View {
        VStack(spacing: 0) {
            Spacer()
            FluentMark()
                .frame(width: 110, height: 110)
                .scaleEffect(appeared ? 1 : 0.8)
                .opacity(appeared ? 1 : 0)
            Text(returning ? "Our terms have changed" : "Welcome to Fluent")
                .font(FluentFont.title(34))
                .foregroundStyle(p.ink)
                .padding(.top, 14)
            Text(returning ? "Please accept the updated terms to keep using Fluent."
                           : "Speak in any app. Fluent writes it for you.")
                .font(FluentFont.body(17))
                .foregroundStyle(p.dim)
                .padding(.top, 8)
            Spacer()

            VStack(spacing: 18) {
                HStack(alignment: .top, spacing: 12) {
                    Button { agreed.toggle() } label: {
                        Image(systemName: agreed ? "checkmark.square.fill" : "square")
                            .font(.system(size: 22))
                            .foregroundStyle(agreed ? p.accent : p.faint)
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("I agree")
                    .accessibilityAddTraits(agreed ? .isSelected : [])
                    Text(agreement)
                        .font(FluentFont.body(15))
                        .foregroundStyle(p.dim)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .onTapGesture { agreed.toggle() }
                }
                Button("Continue") { model.acceptTerms() }
                    .buttonStyle(PrimaryButtonStyle())
                    .disabled(!agreed)
                    .keyboardShortcut(.defaultAction)
            }
            .frame(maxWidth: 440)
            .padding(.bottom, 48)
        }
        .padding(32)
        .onAppear { withAnimation(.spring(duration: 0.8)) { appeared = true } }
    }

    private var agreement: AttributedString {
        var s = AttributedString("I agree to the ")
        var terms = AttributedString("Terms and Conditions")
        terms.link = Constants.termsURL
        terms.foregroundColor = p.accent
        terms.underlineStyle = .single
        var privacy = AttributedString("Privacy Policy")
        privacy.link = Constants.privacyURL
        privacy.foregroundColor = p.accent
        privacy.underlineStyle = .single
        s += terms + AttributedString(" and the ") + privacy + AttributedString(", and I am 18 or older.")
        return s
    }
}

/// Setup in a fixed order, as on Android: each step shows its live state, and permissions are
/// detected the moment they are granted in System Settings.
struct OnboardingView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.palette) private var p
    @State private var keyDraft = ""
    @State private var testResult: String?
    @State private var testing = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                HStack(spacing: 14) {
                    FluentMark().frame(width: 46, height: 46)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Set up Fluent").font(FluentFont.title(30)).foregroundStyle(p.ink)
                        Text("Three steps and you can talk into any app on your Mac.")
                            .font(FluentFont.body(15)).foregroundStyle(p.dim)
                    }
                }
                .padding(.bottom, 6)

                step(1, "Microphone", done: model.micGranted,
                     detail: model.micStatus == .denied
                        ? "Fluent was denied the microphone. Turn Fluent on in System Settings › Privacy & Security › Microphone."
                        : "Fluent listens only while you dictate. Audio goes to Gemini and is never saved.") {
                    Button(model.micStatus == .denied ? "Open System Settings" : "Allow microphone") { model.requestMicrophone() }
                }

                step(2, "Accessibility", done: model.axTrusted,
                     detail: "Lets Fluent see which text box you are in and type your words there. It never reads the screen for anything else. In System Settings, turn on Fluent under Privacy & Security › Accessibility.") {
                    Button("Open System Settings") { model.requestAccessibility() }
                }

                step(3, "Gemini API key", done: model.hasApiKey,
                     detail: "Fluent uses your own free key from Google AI Studio. It is stored in your Mac's Keychain.") {
                    if model.hasApiKey {
                        HStack {
                            Text(model.maskedKey).font(FluentFont.mono(13)).foregroundStyle(p.dim)
                            Button(testing ? "Testing…" : "Test connection") { test() }.disabled(testing)
                            if let testResult { Text(testResult).font(FluentFont.body(13)).foregroundStyle(p.dim) }
                        }
                    } else {
                        HStack {
                            SecureField("Paste your key", text: $keyDraft)
                                .textFieldStyle(.roundedBorder)
                                .frame(maxWidth: 320)
                            Button("Save key") { model.saveApiKey(keyDraft); keyDraft = "" }
                                .disabled(keyDraft.trimmingCharacters(in: .whitespaces).isEmpty)
                            Link("Get a free key", destination: Constants.apiKeyURL)
                        }
                    }
                }

                Card {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("How to dictate").font(FluentFont.title(17)).foregroundStyle(p.ink)
                        howTo("hand.tap", "Click into any text box. A Fluent bubble appears next to it; click it, speak, click Stop.")
                        howTo("keyboard", "Or hold \(model.holdKey.label), speak, and let go to insert.")
                        howTo("command", "Or press \(model.toggleShortcut.label) to start, and again to stop.")
                    }
                }

                HStack {
                    Button("Finish later") { model.setupComplete = true }
                        .buttonStyle(.plain)
                        .foregroundStyle(p.faint)
                    Spacer()
                    Button("Finish setup") { model.setupComplete = true }
                        .buttonStyle(PrimaryButtonStyle())
                        .frame(maxWidth: 260)
                        .disabled(!model.ready)
                }
                .padding(.top, 4)
            }
            .padding(36)
            .frame(maxWidth: 720)
            .frame(maxWidth: .infinity)
        }
    }

    private func test() {
        testing = true
        Task {
            let r = await model.testApiKey()
            testResult = switch r {
            case .success(let m): "Connected to \(m)."
            case .failure(let e): e.userMessage
            }
            testing = false
        }
    }

    private func howTo(_ symbol: String, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: symbol).foregroundStyle(p.accent).frame(width: 20)
            Text(text).font(FluentFont.body(14)).foregroundStyle(p.dim)
        }
    }

    private func step<Actions: View>(_ n: Int, _ title: String, done: Bool, detail: String,
                                     @ViewBuilder actions: () -> Actions) -> some View {
        Card {
            HStack(alignment: .top, spacing: 14) {
                ZStack {
                    Circle().fill(done ? p.success : p.surface)
                        .overlay(Circle().strokeBorder(done ? .clear : p.border, lineWidth: 1))
                    if done {
                        Image(systemName: "checkmark").font(.system(size: 13, weight: .bold)).foregroundStyle(.white)
                    } else {
                        Text("\(n)").font(FluentFont.title(14)).foregroundStyle(p.dim)
                    }
                }
                .frame(width: 28, height: 28)
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Text(title).font(FluentFont.title(17)).foregroundStyle(p.ink)
                        Spacer()
                        Text(done ? "Done" : "Needed").font(FluentFont.mono(11))
                            .foregroundStyle(done ? p.success : p.faint)
                    }
                    Text(detail).font(FluentFont.body(14)).foregroundStyle(p.dim)
                        .fixedSize(horizontal: false, vertical: true)
                    if !done || n == 3 { actions() }
                }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("\(title): \(done ? "done" : "needed")")
    }
}

/// Sidebar plus the four screens.
struct MainView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.palette) private var p

    var body: some View {
        @Bindable var model = model
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 10) {
                    FluentMark().frame(width: 30, height: 30)
                    Text("Fluent").font(FluentFont.title(22)).foregroundStyle(p.ink)
                }
                .padding(.bottom, 18)
                .padding(.leading, 6)
                ForEach(AppModel.Tab.allCases) { tab in
                    Button { model.tab = tab } label: {
                        HStack(spacing: 10) {
                            Image(systemName: tab.symbol).frame(width: 20)
                            Text(tab.title).font(FluentFont.body(15))
                            Spacer()
                        }
                        .padding(.vertical, 8)
                        .padding(.horizontal, 10)
                        .foregroundStyle(model.tab == tab ? p.ink : p.dim)
                        .background {
                            if model.tab == tab {
                                RoundedRectangle(cornerRadius: 10, style: .continuous).fill(p.surface)
                                    .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(p.border))
                            }
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(tab.title)
                }
                Spacer()
                StatusChip()
            }
            .padding(16)
            .frame(width: 210)
            .background(p.surface.opacity(0.5))
            .overlay(alignment: .trailing) { Rectangle().fill(p.border).frame(width: 1) }

            Group {
                switch model.tab {
                case .dictate: DictateView()
                case .history: HistoryView()
                case .style: StyleView()
                case .settings: SettingsView()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

/// Readiness at a glance (Android: the status chip in the header).
struct StatusChip: View {
    @Environment(AppModel.self) private var model
    @Environment(\.palette) private var p

    var body: some View {
        let (color, text): (Color, String) =
            !model.micGranted ? (p.danger, "Microphone needed")
            : !model.axTrusted ? (p.danger, "Accessibility needed")
            : !model.hasApiKey ? (p.voice, "API key needed")
            : model.snoozed ? (p.faint, "Snoozed")
            : (p.success, "Ready")
        return Button { model.tab = .settings } label: {
            HStack(spacing: 8) {
                Circle().fill(color).frame(width: 8, height: 8)
                Text(text).font(FluentFont.mono(11)).foregroundStyle(p.dim)
            }
            .padding(.vertical, 6).padding(.horizontal, 10)
            .background(Capsule().fill(p.surface))
            .overlay(Capsule().strokeBorder(p.border))
        }
        .buttonStyle(.plain)
    }
}
