import Foundation
#if canImport(Glibc)
import Glibc
private let sockStream = Int32(SOCK_STREAM.rawValue)
#else
import Darwin
private let sockStream = SOCK_STREAM
#endif

/// A real HTTP server on 127.0.0.1 (random port) for the Gemini client tests: it records each
/// request exactly as it arrived over the socket and answers with a canned response.
final class MockHTTPServer: @unchecked Sendable {
    struct Request {
        var requestLine: String
        var headers: [String: String]   // lower-cased names
        var body: Data
    }

    let port: UInt16
    private let fd: Int32
    private let lock = NSLock()
    private var _requests: [Request] = []
    private let status: Int
    private let responseBody: String

    var requests: [Request] { lock.lock(); defer { lock.unlock() }; return _requests }
    var baseURL: URL { URL(string: "http://127.0.0.1:\(port)")! }

    init(status: Int = 200, body: String) throws {
        self.status = status
        self.responseBody = body
        signal(SIGPIPE, SIG_IGN)
        let fd = socket(AF_INET, sockStream, 0)
        self.fd = fd
        guard fd >= 0 else { throw URLError(.cannotConnectToHost) }
        var yes: Int32 = 1
        setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &yes, socklen_t(MemoryLayout<Int32>.size))
        var addr = sockaddr_in()
        #if canImport(Darwin)
        addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        #endif
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = 0
        addr.sin_addr.s_addr = inet_addr("127.0.0.1")
        let bound = withUnsafePointer(to: &addr) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
        }
        guard bound == 0, listen(fd, 8) == 0 else { close(fd); throw URLError(.cannotConnectToHost) }
        var len = socklen_t(MemoryLayout<sockaddr_in>.size)
        _ = withUnsafeMutablePointer(to: &addr) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { getsockname(fd, $0, &len) }
        }
        self.port = UInt16(bigEndian: addr.sin_port)
        let thread = Thread { [self] in serve() }
        thread.start()
    }

    func stop() {
        shutdown(fd, Int32(SHUT_RDWR))
        close(fd)
    }

    private func serve() {
        while true {
            let c = accept(fd, nil, nil)
            if c < 0 { return }
            handle(c)
            close(c)
        }
    }

    private func handle(_ c: Int32) {
        var data = Data()
        var buf = [UInt8](repeating: 0, count: 65_536)
        var headerEnd: Range<Data.Index>?
        var contentLength = 0
        while true {
            let n = recv(c, &buf, buf.count, 0)
            if n <= 0 { break }
            data.append(buf, count: n)
            if headerEnd == nil, let r = data.range(of: Data("\r\n\r\n".utf8)) {
                headerEnd = r
                let head = String(decoding: data[..<r.lowerBound], as: UTF8.self)
                for line in head.split(separator: "\r\n").dropFirst() {
                    let parts = line.split(separator: ":", maxSplits: 1)
                    if parts.count == 2, parts[0].lowercased() == "content-length" {
                        contentLength = Int(parts[1].trimmingCharacters(in: .whitespaces)) ?? 0
                    }
                }
            }
            if let r = headerEnd, data.count - r.upperBound >= contentLength { break }
        }
        guard let r = headerEnd else { return }
        let head = String(decoding: data[..<r.lowerBound], as: UTF8.self)
        let lines = head.split(separator: "\r\n").map(String.init)
        var headers: [String: String] = [:]
        for line in lines.dropFirst() {
            let parts = line.split(separator: ":", maxSplits: 1)
            if parts.count == 2 {
                headers[parts[0].lowercased()] = parts[1].trimmingCharacters(in: .whitespaces)
            }
        }
        lock.lock()
        _requests.append(Request(requestLine: lines.first ?? "", headers: headers, body: Data(data[r.upperBound...])))
        lock.unlock()

        let body = Data(responseBody.utf8)
        let reply = "HTTP/1.1 \(status) \(status == 200 ? "OK" : "Error")\r\nContent-Type: application/json\r\n"
            + "Content-Length: \(body.count)\r\nConnection: close\r\n\r\n"
        let out = Data(reply.utf8) + body
        out.withUnsafeBytes { p in
            var sent = 0
            while sent < out.count {
                let n = send(c, p.baseAddress! + sent, out.count - sent, 0)
                if n <= 0 { break }
                sent += n
            }
        }
    }
}
