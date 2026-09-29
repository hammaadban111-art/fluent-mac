import AppKit
import CryptoKit
import FluentMacKit
import Foundation
@preconcurrency import UserNotifications

/// Settings → Updates and the "new version" notice. Reads the website's updates.json at launch and
/// every few hours, notifies once per new version, and updates in place: download the DMG, check its
/// SHA-256 against the manifest, copy the new Fluent.app out of it, then a small script swaps it in
/// after Fluent quits and opens the new version.
@MainActor
@Observable
final class Updater {
    enum Status: Equatable { case idle, checking, upToDate, available, downloading, installing, failed }

    private(set) var status: Status = .idle
    private(set) var latest: ReleaseInfo?
    private(set) var progress: Double = 0
    private(set) var error: String?
    private var timer: Timer?

    static let shared = Updater()

    static var currentVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"
    }

    var summary: String {
        switch status {
        case .checking: "Checking for updates…"
        case .upToDate: "Fluent is up to date (\(Self.currentVersion))."
        case .available: "Fluent \(latest?.version ?? "") is available. You have \(Self.currentVersion)."
        case .downloading: "Downloading Fluent \(latest?.version ?? "")… \(Int(progress * 100))%"
        case .installing: "Installing Fluent \(latest?.version ?? ""). Fluent will reopen."
        case .failed: error ?? "Couldn't check for updates."
        case .idle: "You have Fluent \(Self.currentVersion)."
        }
    }

    /// First check a little after launch, then every six hours.
    func start() {
        Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 15_000_000_000)
            await self?.check(announce: true)
        }
        timer = Timer.scheduledTimer(withTimeInterval: 6 * 3600, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in await self?.check(announce: true) }
        }
    }

    func check(announce: Bool) async {
        if [.checking, .downloading, .installing].contains(status) { return }
        status = .checking
        // FLUENT_UPDATES_URL: tests point Fluent at their own manifest.
        let base = ProcessInfo.processInfo.environment["FLUENT_UPDATES_URL"] ?? Updates.manifestURL.absoluteString
        guard let url = URL(string: base + "?t=\(Int(Date().timeIntervalSince1970))") else { fail("Bad update address."); return }
        var request = URLRequest(url: url)
        request.cachePolicy = .reloadIgnoringLocalCacheData
        request.timeoutInterval = 20
        let fetched = try? await URLSession.shared.data(for: request)
        // An update started meanwhile owns the status now.
        guard status == .checking else { return }
        guard let (data, response) = fetched, (response as? HTTPURLResponse)?.statusCode == 200 else {
            fail("Couldn't reach the website. Check your connection.")
            return
        }
        guard let release = Updates.parse(data) else {
            fail("The update list on the website couldn't be read.")
            return
        }
        latest = release
        status = Updates.isNewer(release.version, than: Self.currentVersion) ? .available : .upToDate
        if status == .available && announce { notifyOnce(release) }
    }

    private func notifyOnce(_ release: ReleaseInfo) {
        let key = "update_notified"
        guard UserDefaults.standard.string(forKey: key) != release.version else { return }
        UserDefaults.standard.set(release.version, forKey: key)
        let title = "Fluent \(release.version) is out"
        let body = release.notes.isEmpty ? "Click to update from Settings → Updates." : release.notes
        let id = "fluent-update-\(release.version)"
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { granted, _ in
            guard granted else { return }
            let content = UNMutableNotificationContent()
            content.title = title
            content.body = body
            UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: id, content: content, trigger: nil))
        }
    }

    func update() async {
        guard let release = latest, ![.checking, .downloading, .installing].contains(status) else { return }
        let appURL = Bundle.main.bundleURL
        // Swapping the app needs write access to its folder (an admin account for /Applications).
        guard FileManager.default.isWritableFile(atPath: appURL.deletingLastPathComponent().path) else {
            NSWorkspace.shared.open(release.url)
            fail("Fluent can't replace itself in \(appURL.deletingLastPathComponent().path). The new version is downloading in your browser: open it and drag Fluent into Applications.")
            return
        }
        status = .downloading
        progress = 0
        do {
            let dir = FileManager.default.temporaryDirectory.appendingPathComponent("Fluent-Update", isDirectory: true)
            try? FileManager.default.removeItem(at: dir)
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let dmg = dir.appendingPathComponent("Fluent-\(release.version).dmg")
            // Off the main thread: downloading, mounting and copying take a few seconds.
            try await Self.download(release, to: dmg) { [weak self] p in
                Task { @MainActor in self?.progress = p }
            }
            status = .installing
            let staged = dir.appendingPathComponent("Fluent.app")
            try await Task.detached { try Self.copyApp(fromDMG: dmg, to: staged) }.value
            try Self.swapAndRelaunch(staged: staged, into: appURL)
            NSApp.terminate(nil)
        } catch {
            fail("The update didn't finish (\(error.localizedDescription)). Try again, or download it from the website.")
        }
    }

    nonisolated private static func download(_ release: ReleaseInfo, to file: URL,
                                             progress: @escaping @Sendable (Double) -> Void) async throws {
        let (bytes, response) = try await URLSession.shared.bytes(from: release.url)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw UpdateError("download failed") }
        let total = response.expectedContentLength > 0 ? response.expectedContentLength : release.size
        FileManager.default.createFile(atPath: file.path, contents: nil)
        let out = try FileHandle(forWritingTo: file)
        defer { try? out.close() }
        var hasher = SHA256()
        var buffer = Data()
        buffer.reserveCapacity(1 << 16)
        var written: Int64 = 0
        for try await byte in bytes {
            buffer.append(byte)
            if buffer.count >= 1 << 16 {
                try out.write(contentsOf: buffer)
                hasher.update(data: buffer)
                written += Int64(buffer.count)
                buffer.removeAll(keepingCapacity: true)
                if total > 0 { progress(min(1, Double(written) / Double(total))) }
            }
        }
        try out.write(contentsOf: buffer)
        hasher.update(data: buffer)
        let hex = hasher.finalize().map { String(format: "%02x", $0) }.joined()
        guard hex == release.sha256 else {
            try? FileManager.default.removeItem(at: file)
            throw UpdateError("the download didn't match its published checksum")
        }
    }

    /// Mounts the DMG read-only, copies Fluent.app out of it, and unmounts it.
    nonisolated private static func copyApp(fromDMG dmg: URL, to staged: URL) throws {
        let mount = FileManager.default.temporaryDirectory.appendingPathComponent("Fluent-Update-mount-\(UUID().uuidString)")
        try run("/usr/bin/hdiutil", ["attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mount.path, dmg.path])
        defer { _ = try? run("/usr/bin/hdiutil", ["detach", mount.path, "-force"]) }
        try run("/usr/bin/ditto", [mount.appendingPathComponent("Fluent.app").path, staged.path])
        try run("/usr/bin/codesign", ["--verify", "--deep", staged.path])
    }

    /// Writes a script that waits for Fluent to quit, swaps the app (keeping the old one if the copy
    /// fails), clears the download quarantine and opens the new version.
    private static func swapAndRelaunch(staged: URL, into appURL: URL) throws {
        let script = FileManager.default.temporaryDirectory.appendingPathComponent("fluent-update-\(UUID().uuidString).sh")
        let q = { (s: String) in "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }
        let body = """
        #!/bin/bash
        PID=\(ProcessInfo.processInfo.processIdentifier)
        for _ in $(seq 1 150); do kill -0 "$PID" 2>/dev/null || break; sleep 0.2; done
        # Still running after 30 s: leave everything as it is rather than swap a live app.
        kill -0 "$PID" 2>/dev/null && { rm -f "$0"; exit 1; }
        DEST=\(q(appURL.path))
        rm -rf "$DEST.old"
        # Never delete the current app unless it was moved aside first.
        if ! mv "$DEST" "$DEST.old"; then /usr/bin/open "$DEST"; rm -f "$0"; exit 1; fi
        if /usr/bin/ditto \(q(staged.path)) "$DEST"; then
          rm -rf "$DEST.old"
        else
          rm -rf "$DEST"; mv "$DEST.old" "$DEST"
        fi
        /usr/bin/xattr -dr com.apple.quarantine "$DEST" 2>/dev/null
        /usr/bin/open "$DEST"
        rm -f "$0"
        """
        try body.write(to: script, atomically: true, encoding: .utf8)
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/bash")
        p.arguments = [script.path]
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try p.run()
    }

    @discardableResult
    nonisolated private static func run(_ tool: String, _ args: [String]) throws -> Int32 {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try p.run()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else { throw UpdateError("\((tool as NSString).lastPathComponent) failed") }
        return p.terminationStatus
    }

    private func fail(_ message: String) {
        error = message
        status = .failed
    }
}

struct UpdateError: LocalizedError {
    let errorDescription: String?
    init(_ message: String) { errorDescription = message }
}

/// Clicking the update notification opens Settings.
final class NotificationOpener: NSObject, UNUserNotificationCenterDelegate, @unchecked Sendable {
    static let shared = NotificationOpener()

    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        DispatchQueue.main.async {
            MainActor.assumeIsolated { AppDelegate.showMainWindow(tab: .settings) }
        }
        completionHandler()
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }
}
