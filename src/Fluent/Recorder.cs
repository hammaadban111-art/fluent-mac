using System;
using System.Collections.Generic;
using System.Threading;
using Fluent.Core;
using Microsoft.Win32;
using NAudio.Wave;

namespace Fluent;

/// <summary>Microphone capture as 16 kHz mono PCM16, the format Gemini is sent (same as Android, iPhone
/// and Mac). The microphone is opened when a dictation starts and released when it ends; nothing is kept
/// between dictations and audio is never written to disk.</summary>
public sealed class Recorder
{
    public enum MicState { Allowed, BlockedByWindows, NoDevice }

    public sealed class StartException(MicState state, string message) : Exception(message)
    {
        public MicState State { get; } = state;
    }

    readonly object gate = new();
    readonly List<short> samples = [];
    bool capturing;
    WaveIn? wave;
    Timer? fake;

    /// <summary>Called on the audio thread with a 0...1 loudness for each buffer.</summary>
    public Action<float>? OnLevel;
    /// <summary>Called on the audio thread with each 16 kHz chunk while capturing (not while paused):
    /// this is what streams to Gemini Live.</summary>
    public Action<short[]>? OnChunk;

    /// <summary>CI only (<c>--fake-mic file.wav</c>): plays a WAV in real time instead of the microphone,
    /// since the build machines have no audio input.</summary>
    public static string? FakeMicPath { get; set; }

    /// <summary>Windows' own switches: Settings › Privacy &amp; security › Microphone (for the whole PC and
    /// for desktop apps), and whether any recording device exists.</summary>
    public static MicState Check()
    {
        if (FakeMicPath is not null) return MicState.Allowed;
        const string store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
        static string? Value(RegistryKey root, string path) { try { return root.OpenSubKey(path)?.GetValue("Value") as string; } catch { return null; } }
        if (Value(Registry.LocalMachine, store) == "Deny" || Value(Registry.CurrentUser, store) == "Deny"
            || Value(Registry.CurrentUser, store + @"\NonPackaged") == "Deny")
            return MicState.BlockedByWindows;
        try { if (WaveIn.DeviceCount == 0) return MicState.NoDevice; } catch { return MicState.NoDevice; }
        return MicState.Allowed;
    }

    public void Start()
    {
        Stop();
        lock (gate) { samples.Clear(); capturing = true; }
        if (FakeMicPath is { } path) { StartFake(path); return; }
        var state = Check();
        if (state != MicState.Allowed) throw new StartException(state, state.ToString());
        var w = new WaveIn
        {
            DeviceNumber = -1,   // the Windows default recording device (wave mapper)
            WaveFormat = new WaveFormat(Constants.SampleRate, 16, 1),
            BufferMilliseconds = 50,
            NumberOfBuffers = 4,
        };
        w.DataAvailable += (_, e) => Handle(e.Buffer, e.BytesRecorded);
        try { w.StartRecording(); }
        catch (Exception e)
        {
            w.Dispose();
            // MMSYSERR_ALLOCATED / NODRIVER, or access denied by the privacy switch.
            throw new StartException(Check() == MicState.BlockedByWindows ? MicState.BlockedByWindows : MicState.NoDevice, e.Message);
        }
        wave = w;
    }

    void StartFake(string path)
    {
        var pcm = Wav.DecodePcm16(System.IO.File.ReadAllBytes(path));
        var pos = 0;
        const int chunk = Constants.SampleRate / 20;   // 50 ms, like the real buffers
        fake = new Timer(_ =>
        {
            var n = Math.Min(chunk, pcm.Length - pos);
            var buf = new byte[chunk * 2];
            if (n > 0) Buffer.BlockCopy(pcm, pos * 2, buf, 0, n * 2);   // then silence
            pos += Math.Max(n, 0);
            Handle(buf, buf.Length);
        }, null, 0, 50);
    }

    public void Pause() { lock (gate) capturing = false; }
    public void Resume() { lock (gate) capturing = true; }

    /// <summary>Releases the microphone and hands over everything captured.</summary>
    public short[] Stop()
    {
        var w = wave; wave = null;
        if (w is not null) { try { w.StopRecording(); } catch { } w.Dispose(); }
        var f = fake; fake = null;
        f?.Dispose();
        lock (gate)
        {
            capturing = false;
            var o = samples.ToArray();
            samples.Clear();
            return o;
        }
    }

    void Handle(byte[] buffer, int bytes)
    {
        var n = bytes / 2;
        if (n == 0) return;
        var chunk = new short[n];
        Buffer.BlockCopy(buffer, 0, chunk, 0, n * 2);
        bool keep;
        lock (gate) keep = capturing;
        double sum = 0;
        foreach (var s in chunk) { var v = s / 32768.0; sum += v * v; }
        var rms = Math.Sqrt(sum / n);
        // Roughly -50 dB..-10 dB mapped onto 0..1, as on the Mac.
        var db = 20 * Math.Log10(Math.Max(rms, 1e-6));
        OnLevel?.Invoke(keep ? (float)Math.Clamp((db + 50) / 40, 0, 1) : 0);
        if (!keep) return;
        lock (gate) { if (!capturing) return; samples.AddRange(chunk); }
        OnChunk?.Invoke(chunk);
    }
}
