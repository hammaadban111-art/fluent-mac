import AppKit
import FluentCore
import FluentMacKit
import SwiftUI

private func copyToClipboard(_ text: String) {
    NSPasteboard.general.clearContents()
    NSPasteboard.general.setString(text, forType: .string)
}

struct ScreenHeader: View {
    var eyebrow: String
    var title: String
    @Environment(\.palette) private var p
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(eyebrow.uppercased()).font(FluentFont.mono(11)).tracking(1.2).foregroundStyle(p.faint)
            Text(title).font(FluentFont.title(30)).foregroundStyle(p.ink)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct DictateView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.palette) private var p
    @State private var copied = false

    private var d: DictationController { model.dictation }

    var body: some View {
        ScrollView {
            VStack(spacing: 18) {
                ScreenHeader(eyebrow: Date.now.formatted(.dateTime.weekday(.wide).day().month(.wide)), title: headline)
                if let blocker { blockerCard(blocker) }
                orb
                if let text = d.lastText { resultCard(text) }
                if let error = d.lastError { errorCard(error) }
                controlsCard
                todayCard
            }
            .padding(28)
            .frame(maxWidth: 760)
            .frame(maxWidth: .infinity)
        }
    }

    private var headline: String {
        switch d.phase {
        case .recording: "Listening."
        case .paused: "Paused."
        case .transcribing: "Writing it down."
        default: model.snoozed ? "Snoozed." : "Ready when you are."
        }
    }

    private var blocker: (String, String, () -> Void)? {
        if !model.micGranted { return ("Fluent needs the microphone", "Allow", { model.requestMicrophone() }) }
        if !model.axTrusted { return ("Turn on Fluent in Accessibility so it can type into other apps", "Open Settings", { model.requestAccessibility() }) }
        if !model.hasApiKey { return ("Add your Gemini API key to start dictating", "Add key", { model.tab = .settings }) }
        return nil
    }

    private func blockerCard(_ b: (String, String, () -> Void)) -> some View {
        Card {
            HStack(spacing: 12) {
                Image(systemName: "exclamationmark.circle.fill").foregroundStyle(p.voice).font(.system(size: 20))
                Text(b.0).font(FluentFont.body(15)).foregroundStyle(p.ink)
                Spacer()
                Button(b.1, action: b.2)
            }
        }
    }

    private var orb: some View {
        VStack(spacing: 14) {
            Button {
                if d.phase.isLive { d.stop() } else { d.start(source: .inApp, target: nil) }
            } label: {
                DictationOrb(size: 170, active: d.phase == .recording,
                             level: d.phase == .recording ? (d.levels.last ?? 0) : 0,
                             symbol: d.phase.isLive ? "stop.fill" : "mic.fill")
            }
            .buttonStyle(.plain)
            .disabled(d.phase == .transcribing)
            .accessibilityLabel(d.phase.isLive ? "Stop dictation" : "Start dictation")

            if d.phase.isLive {
                Text(String(format: "%d:%02d", Int(d.elapsed) / 60, Int(d.elapsed) % 60))
                    .font(FluentFont.mono(15)).foregroundStyle(p.dim)
                HStack(spacing: 12) {
                    Button(d.phase == .paused ? "Resume" : "Pause") { d.togglePause() }
                    Button("Cancel", role: .destructive) { d.cancel() }
                }
            } else if d.phase == .transcribing {
                ProgressView().controlSize(.small)
            } else {
                Text("Click the orb to try it here, or dictate in any app")
                    .font(FluentFont.body(15)).foregroundStyle(p.dim)
            }
        }
        .padding(.vertical, 8)
    }

    private func resultCard(_ text: String) -> some View {
        Card {
            VStack(alignment: .leading, spacing: 12) {
                SectionLabel(text: "Last dictation")
                Text(text).font(FluentFont.body(17)).foregroundStyle(p.ink).textSelection(.enabled)
                HStack {
                    Button(copied ? "Copied" : "Copy", systemImage: copied ? "checkmark" : "doc.on.doc") {
                        copyToClipboard(text)
                        copied = true
                        Task { try? await Task.sleep(nanoseconds: 1_500_000_000); copied = false }
                    }
                    Spacer()
                    Button("Clear", systemImage: "xmark") { d.clearLast() }
                }
            }
        }
    }

    private func errorCard(_ error: TranscriptionError) -> some View {
        Card {
            HStack(alignment: .top, spacing: 12) {
                Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(p.danger)
                Text(error.userMessage).font(FluentFont.body(15)).foregroundStyle(p.ink)
                Spacer()
                if error == .noApiKey || error == .invalidApiKey {
                    Button("Open Settings") { model.tab = .settings }
                } else if error == .micPermission {
                    Button("Allow") { model.requestMicrophone() }
                }
            }
        }
    }

    private var controlsCard: some View {
        @Bindable var model = model
        return Card {
            VStack(alignment: .leading, spacing: 12) {
                Toggle(isOn: $model.bubbleEnabled) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Floating bubble").font(FluentFont.title(16)).foregroundStyle(p.ink)
                        Text("Appears next to the text box you are typing in")
                            .font(FluentFont.body(13)).foregroundStyle(p.dim)
                    }
                }
                .toggleStyle(.switch)
                Divider()
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Shortcuts").font(FluentFont.title(16)).foregroundStyle(p.ink)
                        Text("Hold \(model.holdKey.label) to talk · \(model.toggleShortcut.label) to start and stop")
                            .font(FluentFont.body(13)).foregroundStyle(p.dim)
                    }
                    Spacer()
                }
                Divider()
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Snooze").font(FluentFont.title(16)).foregroundStyle(p.ink)
                        Text(model.snoozed ? model.snoozeLabel : "Hide the bubble and pause the shortcuts for a while")
                            .font(FluentFont.body(13)).foregroundStyle(p.dim)
                    }
                    Spacer()
                    if model.snoozed {
                        Button("Resume") { model.resume() }
                    } else {
                        Menu("Snooze") {
                            ForEach(SnoozeChoice.allCases, id: \.self) { c in
                                Button(c.label) { model.snooze(c) }
                            }
                        }
                        .fixedSize()
                    }
                }
            }
        }
    }

    private var todayCard: some View {
        Card {
            if model.historyEnabled {
                HStack {
                    stat("\(model.wordsToday)", "words today")
                    Spacer()
                    stat("\(model.dictationsToday)", "dictations")
                }
            } else {
                VStack(alignment: .leading, spacing: 4) {
                    Text("History is off").font(FluentFont.title(16)).foregroundStyle(p.ink)
                    Text("Turn it on in Settings to keep transcripts and daily totals on this Mac")
                        .font(FluentFont.body(14)).foregroundStyle(p.dim)
                }
            }
        }
    }

    private func stat(_ value: String, _ label: String) -> some View {
        VStack(alignment: .leading) {
            Text(value).font(FluentFont.title(30)).foregroundStyle(p.ink).contentTransition(.numericText())
            Text(label).font(FluentFont.mono(11)).foregroundStyle(p.faint)
        }
    }
}

struct HistoryView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.palette) private var p
    @State private var query = ""
    @State private var confirmClear = false
    @State private var selected: Transcript?

    private var filtered: [Transcript] {
        query.isEmpty ? model.history : model.history.filter { $0.text.localizedCaseInsensitiveContains(query) }
    }

    private var days: [(Date, [Transcript])] {
        let groups = Dictionary(grouping: filtered) { Calendar.current.startOfDay(for: $0.createdAt) }
        return groups.keys.sorted(by: >).map { ($0, groups[$0]!) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(alignment: .bottom) {
                ScreenHeader(eyebrow: "On this Mac only", title: "History")
                if !model.history.isEmpty {
                    TextField("Search transcripts", text: $query)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 220)
                    Button("Clear all", role: .destructive) { confirmClear = true }
                }
            }
            if !model.historyEnabled && model.history.isEmpty {
                empty("History is off", "Transcripts are only kept on this Mac when you turn history on.",
                      action: ("Turn on history", { model.historyEnabled = true }))
            } else if model.history.isEmpty {
                empty("Nothing yet", "Your dictations will appear here.", action: nil)
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 18) {
                        ForEach(days, id: \.0) { day, items in
                            VStack(alignment: .leading, spacing: 8) {
                                SectionLabel(text: dayTitle(day))
                                ForEach(items) { t in row(t) }
                            }
                        }
                    }
                }
            }
        }
        .padding(28)
        .confirmationDialog("Delete every transcript on this Mac?", isPresented: $confirmClear) {
            Button("Delete all", role: .destructive) { model.clearHistory() }
        }
        .sheet(item: $selected) { t in TranscriptDetail(transcript: t) { selected = nil } }
    }

    private func row(_ t: Transcript) -> some View {
        Button { selected = t } label: {
            Card(padding: 14) {
                VStack(alignment: .leading, spacing: 6) {
                    Text(t.text).lineLimit(3).font(FluentFont.body(15)).foregroundStyle(p.ink)
                        .multilineTextAlignment(.leading)
                    Text("\(t.createdAt.formatted(date: .omitted, time: .shortened)) · \(t.wordCount) words")
                        .font(FluentFont.mono(11)).foregroundStyle(p.faint)
                }
            }
        }
        .buttonStyle(.plain)
        .contextMenu {
            Button("Copy") { copyToClipboard(t.text) }
            Button("Delete", role: .destructive) { model.delete(t) }
        }
    }

    private func dayTitle(_ d: Date) -> String {
        if Calendar.current.isDateInToday(d) { return "Today" }
        if Calendar.current.isDateInYesterday(d) { return "Yesterday" }
        return d.formatted(.dateTime.weekday(.wide).day().month(.wide))
    }

    private func empty(_ title: String, _ detail: String, action: (String, () -> Void)?) -> some View {
        VStack(spacing: 10) {
            Image(systemName: "clock").font(.system(size: 38)).foregroundStyle(p.faint)
            Text(title).font(FluentFont.title(20)).foregroundStyle(p.ink)
            Text(detail).font(FluentFont.body(15)).foregroundStyle(p.dim).multilineTextAlignment(.center)
            if let action { Button(action.0, action: action.1).buttonStyle(.borderedProminent).padding(.top, 6) }
        }
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

struct TranscriptDetail: View {
    let transcript: Transcript
    var close: () -> Void
    @Environment(AppModel.self) private var model
    @Environment(\.palette) private var p
    @State private var confirmDelete = false

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(transcript.createdAt.formatted(date: .complete, time: .shortened))
                .font(FluentFont.mono(12)).foregroundStyle(p.faint)
            ScrollView {
                Text(transcript.text).font(FluentFont.body(17)).foregroundStyle(p.ink).textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            Text("\(transcript.wordCount) words · \(Int(transcript.durationSeconds.rounded())) s")
                .font(FluentFont.mono(12)).foregroundStyle(p.faint)
            HStack {
                Button("Delete", role: .destructive) { confirmDelete = true }
                Spacer()
                Button("Copy") { copyToClipboard(transcript.text) }
                Button("Done", action: close).keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 520, height: 360)
        .background(p.background)
        .confirmationDialog("Delete this transcript?", isPresented: $confirmDelete) {
            Button("Delete", role: .destructive) { model.delete(transcript); close() }
        }
    }
}

/// Per-app styles, as on Android 1.7: Fluent matches how you write where you are writing, from the
/// app in front. Styles only change capitals, punctuation and layout; words are never rewritten.
struct StyleView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.palette) private var p
    @State private var category: StyleCategory = .personal

    var body: some View {
        @Bindable var model = model
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                ScreenHeader(eyebrow: "Per app", title: "Style")
                Text("Fluent matches how you write where you are writing. Styles only change capitals, punctuation and layout. Your words are never rewritten.")
                    .font(FluentFont.body(15)).foregroundStyle(p.dim)

                Card {
                    Toggle(isOn: $model.styleEnabled) {
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Match my style").font(FluentFont.title(16)).foregroundStyle(p.ink)
                            Text(model.mode == .verbatim ? "Off in Verbatim mode, which keeps every word exactly"
                                                         : "Applies in Smart mode")
                                .font(FluentFont.body(13)).foregroundStyle(p.dim)
                        }
                    }
                    .toggleStyle(.switch)
                }

                Picker("Category", selection: $category) {
                    ForEach(StyleCategory.allCases, id: \.self) { Text($0.label).tag($0) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()

                Text("How do you write your \(category.question)?")
                    .font(FluentFont.title(19)).foregroundStyle(p.ink)
                Text("This style applies in \(AppCategories.appliesIn(category))")
                    .font(FluentFont.body(14)).foregroundStyle(p.dim)

                ForEach(category.styles, id: \.self) { style in styleCard(style) }
            }
            .padding(28)
            .frame(maxWidth: 760)
            .frame(maxWidth: .infinity)
            .opacity(model.styleEnabled ? 1 : 0.5)
        }
    }

    private func styleCard(_ style: WritingStyle) -> some View {
        let selected = model.style(for: category) == style
        return Button {
            withAnimation(.snappy) { model.setStyle(style, for: category) }
        } label: {
            Card {
                VStack(alignment: .leading, spacing: 10) {
                    HStack {
                        Text(style.title).font(FluentFont.title(18)).foregroundStyle(p.ink)
                        Spacer()
                        Image(systemName: selected ? "checkmark.circle.fill" : "circle")
                            .foregroundStyle(selected ? p.accent : p.faint)
                            .font(.system(size: 20))
                    }
                    Text(style.rule.uppercased()).font(FluentFont.mono(11)).foregroundStyle(p.faint)
                    Text(StyleFormatter.format(category.sample, style: style, category: category))
                        .font(FluentFont.body(15)).foregroundStyle(p.dim)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .multilineTextAlignment(.leading)
                }
            }
            .overlay {
                RoundedRectangle(cornerRadius: 22, style: .continuous)
                    .strokeBorder(selected ? p.accent : .clear, lineWidth: 2)
            }
        }
        .buttonStyle(.plain)
        .disabled(!model.styleEnabled)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }
}
