import AppKit
import FluentCore
import FluentMacKit
import Observation

/// One dictation at a time, from start to inserted text: the Mac counterpart of Android's
/// `DictationEngine` + `BubbleService` session handling. The recording capsule draws this state.
@MainActor
@Observable
final class DictationController {
    enum Phase: Equatable {
        case idle, recording, paused, transcribing
        case done(String)
        case error(String)

        var isLive: Bool { self == .recording || self == .paused }
        var isBusy: Bool { self != .idle }
    }

    enum Source: Equatable { case bubble, bubbleHold, holdKey, toggle, menu, inApp }

    private(set) var phase: Phase = .idle
    private(set) var source: Source = .toggle
    private(set) var elapsed: TimeInterval = 0
    /// Newest microphone levels, oldest first, for the waveform.
    private(set) var levels: [CGFloat] = Array(repeating: 0.08, count: 18)
    private(set) var lastText: String?
    private(set) var lastError: TranscriptionError?
    /// The app the words will go into, for the capsule and history.
    private(set) var targetAppName: String?

    weak var model: AppModel?
    private let settings: FluentCore.Settings
    private let recorder = Recorder()
    private var target: FocusedField?
    private var category: StyleCategory = .other
    private var runStart: Date?
    private var accumulated: TimeInterval = 0
    private var ticker: Timer?
    private var endTask: Task<Void, Never>?
    private var work: Task<Void, Never>?

    init(settings: FluentCore.Settings) {
        self.settings = settings
        recorder.onLevel = { [weak self] level in
            guard let owner = self else { return }
            Task { @MainActor in owner.push(level) }
        }
    }

    // MARK: control

    /// Starts listening. `target` is the text box the words are for (nil for in-app dictation).
    func start(source: Source, target: FocusedField?) {
        if phase.isLive || phase == .transcribing { return }
        endTask?.cancel()
        guard let model, model.termsAccepted else { return }
        if target?.kind == .secure { return }   // never listen for a password field

        self.source = source
        self.target = target
        targetAppName = target?.appName
        category = AppCategories.category(for: target?.bundleID)
        lastError = nil

        guard let key = KeychainStore.load(), !key.isEmpty else {
            fail(.noApiKey)
            return
        }
        _ = key
        do {
            try recorder.start()
        } catch Recorder.StartError.permission {
            if Recorder.permission == .notDetermined { model.requestMicrophone() }
            fail(.micPermission)
            return
        } catch {
            fail(.micUnavailable)
            return
        }
        accumulated = 0
        runStart = Date()
        elapsed = 0
        levels = Array(repeating: 0.08, count: levels.count)
        phase = .recording
        if model.soundsEnabled { Sounds.play("Tink") }
        startTicker()
    }

    func togglePause() {
        switch phase {
        case .recording:
            recorder.pause()
            accumulated += runStart.map { Date().timeIntervalSince($0) } ?? 0
            runStart = nil
            phase = .paused
        case .paused:
            recorder.resume()
            runStart = Date()
            phase = .recording
        default: break
        }
    }

    /// Ends the recording and writes the text into the field.
    func stop() {
        guard phase.isLive else { return }
        ticker?.invalidate()
        updateElapsed()
        let samples = recorder.stop()
        if model?.soundsEnabled == true { Sounds.play("Pop") }
        let duration = WAV.duration(sampleCount: samples.count)
        guard duration >= Constants.minRecordingSeconds else {
            fail(.tooShort)
            return
        }
        phase = .transcribing
        let wav = WAV.encode(pcm16: samples)
        let key = KeychainStore.load() ?? ""
        let mode = settings.mode, languages = settings.languageCodes, vocabulary = settings.vocabulary
        work = Task { [weak self] in
            let result = await GeminiClient(apiKey: key).transcribe(
                wav: wav, mode: mode, languageCodes: languages, vocabulary: vocabulary)
            await self?.finish(result, duration: duration)
        }
    }

    func cancel() {
        ticker?.invalidate()
        work?.cancel()
        recorder.stop()
        phase = .idle
    }

    /// Bubble tap, toggle shortcut and menu: start when idle, stop when listening.
    func toggle(source: Source, target: FocusedField?) {
        if phase.isLive { stop() } else { start(source: source, target: target) }
    }

    func clearLast() {
        lastText = nil
        lastError = nil
    }

    // MARK: finishing

    private func finish(_ result: Result<String, TranscriptionError>, duration: TimeInterval) async {
        guard phase == .transcribing else { return }   // cancelled meanwhile
        switch result {
        case .failure(let error):
            fail(error)
        case .success(let raw):
            let text = settings.applyStyle(raw, category: category)
            lastText = text
            model?.record(Transcript(text: text, durationSeconds: duration))
            if source == .inApp {
                phase = .done("Done")
                endAfter(0.75)
                return
            }
            let outcome = await TextInserter.insert(text, fallback: target)
            switch outcome {
            case .accessibility, .pasted:
                phase = .done("Inserted")
                endAfter(0.75)
            case .failed:
                // Never lose a transcript: it stays on the clipboard to paste by hand.
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(text, forType: .string)
                phase = .done("Copied — press ⌘V to paste")
                endAfter(2.2)
            }
        }
    }

    private func fail(_ error: TranscriptionError) {
        ticker?.invalidate()
        recorder.stop()
        lastError = error
        phase = .error(error.userMessage)
        endAfter(2.6)
    }

    private func endAfter(_ seconds: Double) {
        endTask?.cancel()
        endTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
            guard !Task.isCancelled, let self else { return }
            if !self.phase.isLive && self.phase != .transcribing { self.phase = .idle }
        }
    }

    // MARK: timer and levels

    private func startTicker() {
        ticker?.invalidate()
        ticker = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
    }

    private func tick() {
        updateElapsed()
        if elapsed > Constants.maxRecordingSeconds { stop() }
    }

    private func updateElapsed() {
        elapsed = accumulated + (runStart.map { Date().timeIntervalSince($0) } ?? 0)
    }

    private func push(_ level: Float) {
        guard phase == .recording else { return }
        levels.removeFirst()
        levels.append(CGFloat(min(1, max(0.06, level))))
    }

    // MARK: demo

    /// `--demo capsule-*`: shows a phase with a synthetic level so the capsule can be screenshotted
    /// on a machine with no microphone. Never used in normal runs.
    func showDemo(_ phase: Phase, appName: String? = nil) {
        self.phase = phase
        targetAppName = appName
        elapsed = 7
        let pattern: [CGFloat] = [0.2, 0.45, 0.8, 0.55, 0.95, 0.6, 0.35, 0.7, 0.5, 0.85, 0.4, 0.65, 0.3, 0.9, 0.55, 0.45, 0.7, 0.35]
        levels = pattern
        if phase == .recording {
            demoClock = 0
            ticker?.invalidate()
            ticker = Timer.scheduledTimer(withTimeInterval: 0.08, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.demoTick() }
            }
        }
    }

    private var demoClock = 0.0

    private func demoTick() {
        demoClock += 0.08
        let t = demoClock
        levels.removeFirst()
        levels.append(CGFloat(0.35 + 0.5 * abs(sin(t * 2.3) * cos(t * 0.7))))
        elapsed = 7 + t
    }
}
