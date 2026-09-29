import FluentCore
import Foundation

/// Picks the transcript after stop: live first (it is normally final well under a second after stop);
/// if live has not answered within `grace`, the batch request with the whole recording starts as well
/// and the first usable answer wins, so a slow live finish never holds up the words for long.
public enum TranscriptRace {
    public static let grace: TimeInterval = 1.2

    public static func run(
        live: (@Sendable () async -> String?)?,
        batch: @escaping @Sendable () async -> Result<String, TranscriptionError>,
        grace: TimeInterval = TranscriptRace.grace
    ) async -> (result: Result<String, TranscriptionError>, route: String) {
        guard let live else { return (await batch(), "batch") }
        let liveTask = Task { await live() }

        let early = await first([
            { .live(await liveTask.value) },
            {
                try? await Task.sleep(nanoseconds: UInt64(grace * 1_000_000_000))
                return .timer
            },
        ])
        if case .live(let text) = early {
            if let text { return (.success(text), "live") }
            return (await batch(), "batch")
        }

        let batchTask = Task { await batch() }
        let second = await first([{ .live(await liveTask.value) }, { .batch(await batchTask.value) }])
        switch second {
        case .live(let text?): return (.success(text), "live")
        case .batch(let r) where (try? r.get()) != nil: return (r, "batch (live slow)")
        default: break
        }
        let b = await batchTask.value
        if (try? b.get()) != nil { return (b, "batch (live slow)") }
        // Batch failed: the live answer may still come.
        if let late = await liveTask.value { return (.success(late), "live") }
        return (b, "batch")
    }

    enum First: Sendable { case live(String?), timer, batch(Result<String, TranscriptionError>) }

    /// The first of `ops` to finish. The others keep running unobserved (a task group would wait for them).
    static func first(_ ops: [@Sendable () async -> First]) async -> First {
        await withCheckedContinuation { (c: CheckedContinuation<First, Never>) in
            let once = Once(c)
            for op in ops { Task { once.resume(await op()) } }
        }
    }

    private final class Once: @unchecked Sendable {
        private let lock = NSLock()
        private var continuation: CheckedContinuation<First, Never>?
        init(_ c: CheckedContinuation<First, Never>) { continuation = c }
        func resume(_ value: First) {
            lock.lock()
            let c = continuation
            continuation = nil
            lock.unlock()
            c?.resume(returning: value)
        }
    }
}
