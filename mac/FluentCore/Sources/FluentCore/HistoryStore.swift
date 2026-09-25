import Foundation

public struct Transcript: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var text: String
    public var createdAt: Date
    public var durationSeconds: Double

    public init(id: UUID = UUID(), text: String, createdAt: Date = Date(), durationSeconds: Double) {
        self.id = id
        self.text = text
        self.createdAt = createdAt
        self.durationSeconds = durationSeconds
    }

    public var wordCount: Int {
        text.split { $0.isWhitespace || $0.isNewline }.count
    }
}

/// Local transcript history: text only, one JSON file in the App Group container, newest first.
/// It is written only when history is switched on.
public final class HistoryStore: @unchecked Sendable {
    public static let limit = 500

    private let url: URL
    private let queue = DispatchQueue(label: "fluent.history")

    public init(directory: URL? = HistoryStore.defaultDirectory) {
        let dir = directory ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        url = dir.appendingPathComponent("history.json")
    }

    /// iOS shares the App Group with the keyboard. The Mac app has no keyboard, so its history
    /// lives in its own Application Support folder (see `HistoryStore(directory:)` in the Mac app).
    public static var defaultDirectory: URL? {
        #if os(iOS)
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: Constants.appGroup)
        #else
        nil
        #endif
    }

    public func all() -> [Transcript] {
        queue.sync { load() }
    }

    public func add(_ transcript: Transcript) {
        queue.sync {
            var items = load()
            items.insert(transcript, at: 0)
            save(Array(items.prefix(Self.limit)))
        }
    }

    public func delete(_ id: UUID) {
        queue.sync { save(load().filter { $0.id != id }) }
    }

    public func clear() {
        queue.sync { save([]) }
    }

    private func load() -> [Transcript] {
        guard let data = try? Data(contentsOf: url) else { return [] }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return (try? decoder.decode([Transcript].self, from: data)) ?? []
    }

    private func save(_ items: [Transcript]) {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(items) else { return }
        #if os(iOS)
        try? data.write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        #else
        try? data.write(to: url, options: [.atomic])
        #endif
    }
}
