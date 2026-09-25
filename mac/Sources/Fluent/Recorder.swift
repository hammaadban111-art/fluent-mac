import AVFoundation
import FluentCore

/// Microphone capture as 16 kHz mono PCM16, the format Gemini is sent (same as Android and iPhone).
/// The microphone is opened when a dictation starts and released when it ends; nothing is kept
/// between dictations and audio is never written to disk.
final class Recorder {
    enum StartError: Error { case permission, unavailable(String) }

    private var engine: AVAudioEngine?
    private let lock = NSLock()
    private var samples: [Int16] = []
    private var capturing = false
    private var converter: AVAudioConverter?
    private let target = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: Constants.sampleRate,
                                       channels: 1, interleaved: true)!

    /// Called on the audio thread with a 0...1 loudness for each buffer.
    var onLevel: ((Float) -> Void)?

    static var permission: AVAuthorizationStatus { AVCaptureDevice.authorizationStatus(for: .audio) }

    static func requestPermission() async -> Bool {
        await AVCaptureDevice.requestAccess(for: .audio)
    }

    func start() throws {
        stop()
        guard Self.permission == .authorized else { throw StartError.permission }
        let engine = AVAudioEngine()
        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        guard format.sampleRate > 0, format.channelCount > 0 else { throw StartError.unavailable("no input device") }
        converter = AVAudioConverter(from: format, to: target)
        lock.lock(); samples.removeAll(keepingCapacity: true); capturing = true; lock.unlock()
        input.installTap(onBus: 0, bufferSize: 4096, format: format) { [weak self] buffer, _ in
            self?.handle(buffer)
        }
        engine.prepare()
        do {
            try engine.start()
        } catch {
            input.removeTap(onBus: 0)
            throw StartError.unavailable(error.localizedDescription)
        }
        self.engine = engine
    }

    func pause() { lock.lock(); capturing = false; lock.unlock() }
    func resume() { lock.lock(); capturing = true; lock.unlock() }

    /// Releases the microphone and hands over everything captured.
    @discardableResult
    func stop() -> [Int16] {
        if let engine {
            engine.inputNode.removeTap(onBus: 0)
            engine.stop()
        }
        engine = nil
        lock.lock(); defer { lock.unlock() }
        capturing = false
        let out = samples
        samples.removeAll()
        return out
    }

    private func handle(_ buffer: AVAudioPCMBuffer) {
        lock.lock(); let keep = capturing; lock.unlock()
        if let channel = buffer.floatChannelData?[0] {
            var sum: Float = 0
            let n = Int(buffer.frameLength)
            for i in 0..<n { sum += channel[i] * channel[i] }
            let rms = n > 0 ? sqrt(sum / Float(n)) : 0
            // Roughly -50 dB..-10 dB mapped onto 0..1.
            let db = 20 * log10(max(rms, 1e-6))
            onLevel?(keep ? min(1, max(0, (db + 50) / 40)) : 0)
        }
        guard keep, let converter else { return }

        let ratio = target.sampleRate / buffer.format.sampleRate
        let capacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio) + 32
        guard let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
        var fed = false
        var error: NSError?
        converter.convert(to: out, error: &error) { _, status in
            if fed { status.pointee = .noDataNow; return nil }
            fed = true
            status.pointee = .haveData
            return buffer
        }
        guard error == nil, let data = out.int16ChannelData?[0] else { return }
        let chunk = Array(UnsafeBufferPointer(start: data, count: Int(out.frameLength)))
        lock.lock(); if capturing { samples.append(contentsOf: chunk) }; lock.unlock()
    }
}
