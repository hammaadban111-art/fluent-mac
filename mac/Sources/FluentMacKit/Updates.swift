import Foundation

/// One platform's entry in the website's updates.json, published with every release.
public struct ReleaseInfo: Equatable, Sendable {
    public var version: String
    public var url: URL
    public var sha256: String
    public var size: Int64
    public var notes: String
}

/// Reads updates.json and compares versions. No AppKit, so it is unit-tested.
public enum Updates {
    public static let manifestURL = URL(string: "https://fluent-voice-v2.vercel.app/updates.json")!

    /// The entry for `platform` ("mac", "windows", "android"), or nil. Only HTTPS downloads count.
    public static func parse(_ data: Data, platform: String = "mac") -> ReleaseInfo? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let p = root[platform] as? [String: Any],
              let version = (p["version"] as? String)?.trimmingCharacters(in: .whitespaces), !version.isEmpty,
              let urlString = p["url"] as? String, let url = URL(string: urlString), url.scheme == "https",
              let sha = (p["sha256"] as? String)?.trimmingCharacters(in: .whitespaces), !sha.isEmpty
        else { return nil }
        let size = (p["size"] as? NSNumber)?.int64Value ?? 0
        return ReleaseInfo(version: version, url: url, sha256: sha.lowercased(), size: size, notes: p["notes"] as? String ?? "")
    }

    /// Whether `latest` is later than `current`: dotted numbers part by part, missing parts as 0.
    public static func isNewer(_ latest: String, than current: String) -> Bool {
        let a = parts(latest), b = parts(current)
        for i in 0..<max(a.count, b.count) {
            let x = i < a.count ? a[i] : 0, y = i < b.count ? b[i] : 0
            if x != y { return x > y }
        }
        return false
    }

    static func parts(_ v: String) -> [Int] {
        var s = v.trimmingCharacters(in: .whitespaces)
        if s.first == "v" || s.first == "V" { s.removeFirst() }
        var out: [Int] = []
        for part in s.split(whereSeparator: { $0 == "." || $0 == "-" || $0 == "+" }) {
            guard let n = Int(part) else { break }
            out.append(n)
        }
        return out
    }
}
