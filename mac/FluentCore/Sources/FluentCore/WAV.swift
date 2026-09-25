import Foundation

/// Wraps raw 16-bit little-endian mono PCM in a RIFF/WAVE header.
public enum WAV {
    public static func encode(pcm16 samples: [Int16], sampleRate: Int = Int(Constants.sampleRate)) -> Data {
        let dataBytes = samples.count * 2
        var d = Data(capacity: 44 + dataBytes)
        func u32(_ v: UInt32) { withUnsafeBytes(of: v.littleEndian) { d.append(contentsOf: $0) } }
        func u16(_ v: UInt16) { withUnsafeBytes(of: v.littleEndian) { d.append(contentsOf: $0) } }
        d.append(contentsOf: Array("RIFF".utf8)); u32(UInt32(36 + dataBytes))
        d.append(contentsOf: Array("WAVE".utf8))
        d.append(contentsOf: Array("fmt ".utf8)); u32(16)
        u16(1)                          // PCM
        u16(1)                          // mono
        u32(UInt32(sampleRate))
        u32(UInt32(sampleRate * 2))     // byte rate
        u16(2)                          // block align
        u16(16)                         // bits per sample
        d.append(contentsOf: Array("data".utf8)); u32(UInt32(dataBytes))
        samples.withUnsafeBufferPointer { buf in
            for s in buf { withUnsafeBytes(of: s.littleEndian) { d.append(contentsOf: $0) } }
        }
        return d
    }

    public static func duration(sampleCount: Int, sampleRate: Double = Constants.sampleRate) -> TimeInterval {
        Double(sampleCount) / sampleRate
    }
}
