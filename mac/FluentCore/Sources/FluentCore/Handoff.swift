import Foundation

/// The app and the keyboard are two processes. iOS does not let a keyboard record audio, so the
/// app records and the keyboard only asks and inserts. They talk through the App Group:
///
/// - the keyboard writes a `Command` (start / stop / cancel) and rings `commandNotification`;
/// - the app writes its `SessionSnapshot` (state, live level, heartbeat) and rings `stateNotification`;
/// - when a transcript is ready the app writes a `Delivery` tagged with the command's id, and the
///   keyboard inserts it once.
///
/// Darwin notifications carry no payload, so every message lives in shared UserDefaults and the
/// notification only says "look now".
public enum SessionState: String, Codable, Sendable {
    /// No session: the keyboard has to open the app to start one.
    case off
    /// The microphone is live but nothing is kept; waiting for the keyboard to say start.
    case ready
    case recording
    case transcribing
}

public struct Command: Codable, Equatable, Sendable {
    public enum Action: String, Codable, Sendable { case start, stop, cancel, pause, resume }
    public var id: String
    public var action: Action
    public var category: StyleCategory
    public var at: Date

    public init(id: String = UUID().uuidString, action: Action, category: StyleCategory, at: Date = Date()) {
        self.id = id
        self.action = action
        self.category = category
        self.at = at
    }
}

public struct SessionSnapshot: Codable, Equatable, Sendable {
    public var state: SessionState
    /// The id of the dictation being recorded or transcribed, so the keyboard knows it is its own.
    public var dictationID: String?
    public var level: Float
    public var startedAt: Date?
    /// Recorded time so far, paused time excluded, as of `heartbeat`.
    public var elapsed: TimeInterval
    public var paused: Bool
    public var heartbeat: Date

    public init(state: SessionState, dictationID: String? = nil, level: Float = 0,
                startedAt: Date? = nil, elapsed: TimeInterval = 0, paused: Bool = false,
                heartbeat: Date = Date()) {
        self.state = state
        self.dictationID = dictationID
        self.level = level
        self.startedAt = startedAt
        self.elapsed = elapsed
        self.paused = paused
        self.heartbeat = heartbeat
    }

    /// The timer the keyboard shows: the last published value, run forward while recording.
    public func elapsed(at now: Date) -> TimeInterval {
        guard state == .recording, !paused else { return elapsed }
        return elapsed + max(0, now.timeIntervalSince(heartbeat))
    }
}

public struct Delivery: Codable, Equatable, Sendable {
    public var dictationID: String
    public var text: String?
    public var error: String?
    public var at: Date

    public init(dictationID: String, text: String?, error: String?, at: Date = Date()) {
        self.dictationID = dictationID
        self.text = text
        self.error = error
        self.at = at
    }
}

public struct Handoff {
    public static let commandNotification = "com.hammaad.fluent.command"
    public static let stateNotification = "com.hammaad.fluent.state"
    /// The app beats every second while a session is up; older than this means it is gone
    /// (killed, crashed or suspended), whatever the stored state says.
    public static let heartbeatTimeout: TimeInterval = 3.5
    /// A delivery older than this is not inserted: the user has long moved on.
    public static let deliveryTimeout: TimeInterval = 120

    private enum Key {
        static let command = "handoff_command"
        static let snapshot = "handoff_snapshot"
        static let delivery = "handoff_delivery"
        static let consumed = "handoff_consumed"
    }

    public let defaults: UserDefaults
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()

    public init(defaults: UserDefaults? = UserDefaults(suiteName: Constants.appGroup)) {
        self.defaults = defaults ?? .standard
    }

    // MARK: keyboard → app

    public func send(_ command: Command) {
        write(command, Key.command)
        DarwinNotifier.post(Self.commandNotification)
    }

    public func latestCommand() -> Command? { read(Key.command) }

    // MARK: app → keyboard

    public func publish(_ snapshot: SessionSnapshot, notify: Bool = true) {
        write(snapshot, Key.snapshot)
        if notify { DarwinNotifier.post(Self.stateNotification) }
    }

    /// The session as the keyboard should believe it: `off` when the heartbeat has stopped.
    public func snapshot(now: Date = Date()) -> SessionSnapshot {
        guard let s: SessionSnapshot = read(Key.snapshot),
              now.timeIntervalSince(s.heartbeat) < Self.heartbeatTimeout else {
            return SessionSnapshot(state: .off, heartbeat: .distantPast)
        }
        return s
    }

    public func deliver(_ delivery: Delivery) {
        write(delivery, Key.delivery)
        DarwinNotifier.post(Self.stateNotification)
    }

    /// Returns a delivery at most once, and only while it is fresh.
    public func takeDelivery(now: Date = Date()) -> Delivery? {
        guard let d: Delivery = read(Key.delivery),
              defaults.string(forKey: Key.consumed) != d.dictationID,
              now.timeIntervalSince(d.at) < Self.deliveryTimeout else { return nil }
        defaults.set(d.dictationID, forKey: Key.consumed)
        return d
    }

    // MARK: storage

    private func write<T: Encodable>(_ value: T, _ key: String) {
        if let data = try? encoder.encode(value) { defaults.set(data, forKey: key) }
    }

    private func read<T: Decodable>(_ key: String) -> T? {
        guard let data = defaults.data(forKey: key) else { return nil }
        return try? decoder.decode(T.self, from: data)
    }
}

/// Cross-process "look now" pings. Payload-free by design (see `Handoff`).
#if canImport(Darwin)
public final class DarwinNotifier: @unchecked Sendable {
    public static func post(_ name: String) {
        CFNotificationCenterPostNotification(
            CFNotificationCenterGetDarwinNotifyCenter(), CFNotificationName(name as CFString), nil, nil, true)
    }

    private let name: String
    private let handler: () -> Void

    /// Calls `handler` on the main queue every time `name` is posted, until deinit.
    public init(observing name: String, handler: @escaping () -> Void) {
        self.name = name
        self.handler = handler
        CFNotificationCenterAddObserver(
            CFNotificationCenterGetDarwinNotifyCenter(),
            Unmanaged.passUnretained(self).toOpaque(),
            { _, observer, _, _, _ in
                guard let observer else { return }
                let me = Unmanaged<DarwinNotifier>.fromOpaque(observer).takeUnretainedValue()
                DispatchQueue.main.async { me.handler() }
            },
            name as CFString, nil, .deliverImmediately)
    }

    deinit {
        CFNotificationCenterRemoveObserver(
            CFNotificationCenterGetDarwinNotifyCenter(), Unmanaged.passUnretained(self).toOpaque(),
            CFNotificationName(name as CFString), nil)
    }
}
#else
/// Darwin notifications do not exist on Linux, where only the unit tests run; posting is a no-op.
public final class DarwinNotifier: @unchecked Sendable {
    public static func post(_ name: String) {}
    public init(observing name: String, handler: @escaping () -> Void) {}
}
#endif

/// Makes an inserted transcript sit naturally against the text already before the cursor.
public enum InsertionSpacing {
    public static func prepare(_ text: String, before: String?) -> String {
        guard let last = before?.last, !text.isEmpty else { return text }
        if last.isWhitespace || "([{“‘\"'/".contains(last) { return text }
        if let first = text.first, ".,;:!?)]}”’".contains(first) { return text }
        return " " + text
    }
}
