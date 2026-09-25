import AppKit
import FluentCore
import FluentMacKit
import SwiftUI

struct SettingsView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.palette) private var p
    @State private var keyDraft = ""
    @State private var editingKey = false
    @State private var testResult: String?
    @State private var testing = false
    @State private var newWord = ""

    var body: some View {
        @Bindable var model = model
        Form {
            Section {
                Picker("Mode", selection: $model.mode) {
                    ForEach(TranscriptionMode.allCases, id: \.self) { Text($0.label).tag($0) }
                }
                Text(model.mode.detail).font(FluentFont.body(12)).foregroundStyle(p.dim)
                Picker("Language", selection: $model.languageCode) {
                    ForEach(languageOptions, id: \.code) { Text($0.name).tag($0.code) }
                }
                LabeledContent("Custom vocabulary") {
                    HStack {
                        TextField("Add a name or word", text: $newWord)
                            .frame(width: 200)
                            .onSubmit(addWord)
                        Button("Add", action: addWord)
                            .disabled(newWord.trimmingCharacters(in: .whitespaces).isEmpty)
                    }
                }
                if !model.vocabulary.isEmpty {
                    FlowWords(words: model.vocabulary) { w in model.vocabulary.removeAll { $0 == w } }
                }
            } header: {
                Text("Dictation")
            } footer: {
                Text("Vocabulary: names, brands and jargon Gemini should spell your way.")
            }

            Section {
                Picker("Hold to talk", selection: $model.holdKey) {
                    ForEach(HoldKey.allCases, id: \.self) { Text($0.label).tag($0) }
                }
                Picker("Start / stop shortcut", selection: $model.toggleShortcut) {
                    ForEach(ToggleShortcut.presets) { Text($0.label).tag($0) }
                    Text(ToggleShortcut.off.label).tag(ToggleShortcut.off)
                }
            } header: {
                Text("Shortcuts")
            } footer: {
                Text(model.holdKey == .fn
                     ? "Using Fn: set System Settings › Keyboard › “Press 🌐 key to” to “Do Nothing” so macOS does not open its own dictation or emoji picker. Esc cancels a dictation."
                     : "Hold the key, speak, and let go to insert. Esc cancels a dictation.")
            }

            Section("Floating bubble") {
                Toggle("Show the bubble next to text boxes", isOn: $model.bubbleEnabled)
                LabeledContent("Size") {
                    Slider(value: $model.bubbleSize, in: 34...60).frame(width: 200)
                }
                LabeledContent("Opacity") {
                    Slider(value: $model.bubbleOpacity, in: 0.35...1).frame(width: 200)
                }
                Picker("Snooze length", selection: $model.snoozeChoice) {
                    ForEach(SnoozeChoice.allCases, id: \.self) { Text($0.label).tag($0) }
                }
                LabeledContent("Snooze") {
                    if model.snoozed {
                        HStack { Text(model.snoozeLabel).foregroundStyle(p.dim); Button("Resume") { model.resume() } }
                    } else {
                        Button("Snooze now") { model.snooze() }
                    }
                }
                LabeledContent("Hidden in") {
                    Menu("Add app") {
                        ForEach(runningApps, id: \.0) { app in
                            Button(app.1) { model.exclude(app.0) }
                        }
                    }
                    .fixedSize()
                }
                ForEach(model.excludedApps, id: \.self) { id in
                    HStack {
                        Text(appName(id)).foregroundStyle(p.ink)
                        Text(id).font(FluentFont.mono(11)).foregroundStyle(p.faint)
                        Spacer()
                        Button("Remove") { model.excludedApps.removeAll { $0 == id } }
                    }
                }
            }

            Section("Theme") {
                HStack(spacing: 14) {
                    ForEach(Palette.all) { theme in themeSwatch(theme) }
                }
                .padding(.vertical, 4)
                Picker("Bubble and capsule colours", selection: $model.overlayThemeID) {
                    Text("Match app theme").tag(MacSettings.matchApp)
                    ForEach(Palette.all) { Text($0.name).tag($0.id) }
                }
            }

            Section {
                if editingKey || !model.hasApiKey {
                    SecureField("Paste your key", text: $keyDraft)
                    HStack {
                        Button("Save key") {
                            model.saveApiKey(keyDraft); keyDraft = ""; editingKey = false; testResult = nil
                        }
                        .disabled(keyDraft.trimmingCharacters(in: .whitespaces).isEmpty)
                        if model.hasApiKey { Button("Cancel") { editingKey = false } }
                        Spacer()
                        Link("Get a free key from Google AI Studio", destination: Constants.apiKeyURL)
                    }
                } else {
                    LabeledContent("Key") { Text(model.maskedKey).font(FluentFont.mono(12)) }
                    HStack {
                        Button(testing ? "Testing…" : "Test connection") {
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
                        .disabled(testing)
                        Button("Replace key") { editingKey = true }
                        Spacer()
                        Button("Remove key", role: .destructive) { model.saveApiKey(""); testResult = nil }
                    }
                    if let testResult { Text(testResult).font(FluentFont.body(12)).foregroundStyle(p.dim) }
                }
            } header: {
                Text("Gemini API key")
            } footer: {
                Text("Stored in your Mac's Keychain. Model: \(Constants.batchModel).")
            }

            Section {
                permissionRow("Microphone", granted: model.micGranted) { model.requestMicrophone() }
                permissionRow("Accessibility", granted: model.axTrusted) { model.requestAccessibility() }
            } header: {
                Text("Permissions")
            } footer: {
                Text("Accessibility is used for two things only: finding the text box you are in, and typing your words there.")
            }

            Section {
                Toggle("Keep history", isOn: $model.historyEnabled)
            } header: {
                Text("Privacy")
            } footer: {
                Text("Off by default. History stays on this Mac and holds text only. Audio is never saved.")
            }

            Section("General") {
                Toggle("Open Fluent at login", isOn: Binding(get: { model.launchAtLogin },
                                                            set: { model.setLaunchAtLogin($0) }))
                if let e = model.launchAtLoginError {
                    Text(e).font(FluentFont.body(12)).foregroundStyle(p.danger)
                }
                Toggle("Start and stop sounds", isOn: $model.soundsEnabled)
            }

            Section("Legal") {
                Link("Terms and Conditions", destination: Constants.termsURL)
                Link("Privacy Policy", destination: Constants.privacyURL)
                LabeledContent("Version", value: appVersion)
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
    }

    private var runningApps: [(String, String)] {
        NSWorkspace.shared.runningApplications
            .filter { $0.activationPolicy == .regular && $0.bundleIdentifier != Bundle.main.bundleIdentifier }
            .compactMap { app in app.bundleIdentifier.map { ($0, app.localizedName ?? $0) } }
            .filter { !model.excludedApps.contains($0.0) }
            .sorted { $0.1.localizedCaseInsensitiveCompare($1.1) == .orderedAscending }
    }

    private func appName(_ id: String) -> String {
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) else { return id }
        return FileManager.default.displayName(atPath: url.path).replacingOccurrences(of: ".app", with: "")
    }

    private func permissionRow(_ title: String, granted: Bool, action: @escaping () -> Void) -> some View {
        LabeledContent(title) {
            HStack {
                Circle().fill(granted ? p.success : p.danger).frame(width: 8, height: 8)
                Text(granted ? "Allowed" : "Not allowed").foregroundStyle(p.dim)
                if !granted { Button("Allow…", action: action) }
            }
        }
    }

    private func addWord() {
        let w = newWord.trimmingCharacters(in: .whitespaces)
        guard !w.isEmpty, !model.vocabulary.contains(w) else { return }
        model.vocabulary.append(w)
        newWord = ""
    }

    private func themeSwatch(_ theme: Palette) -> some View {
        let selected = theme.id == model.themeID
        return Button {
            withAnimation(.easeInOut) { model.themeID = theme.id }
        } label: {
            VStack(spacing: 6) {
                ZStack {
                    RoundedRectangle(cornerRadius: 12, style: .continuous).fill(theme.background)
                    Circle()
                        .fill(AngularGradient(colors: theme.orb + [theme.orb[0]], center: .center))
                        .frame(width: 28, height: 28)
                        .blur(radius: 2)
                }
                .frame(width: 60, height: 60)
                .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .strokeBorder(selected ? p.accent : theme.border.opacity(2), lineWidth: selected ? 2.5 : 1))
                Text(theme.name).font(FluentFont.body(12)).foregroundStyle(p.ink)
                Text(theme.tagline).font(FluentFont.body(10)).foregroundStyle(p.faint)
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(theme.name) theme")
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private var appVersion: String {
        let info = Bundle.main.infoDictionary
        return "\(info?["CFBundleShortVersionString"] as? String ?? "dev") (\(info?["CFBundleVersion"] as? String ?? "0"))"
    }
}

/// Vocabulary words as removable chips.
struct FlowWords: View {
    var words: [String]
    var remove: (String) -> Void
    @Environment(\.palette) private var p

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                ForEach(words, id: \.self) { w in
                    HStack(spacing: 4) {
                        Text(w).font(FluentFont.body(12))
                        Button { remove(w) } label: { Image(systemName: "xmark").font(.system(size: 9, weight: .bold)) }
                            .buttonStyle(.plain)
                            .accessibilityLabel("Remove \(w)")
                    }
                    .padding(.vertical, 4).padding(.horizontal, 8)
                    .background(Capsule().fill(p.surface))
                    .overlay(Capsule().strokeBorder(p.border))
                }
            }
        }
    }
}
