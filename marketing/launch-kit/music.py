"""Soundtracks for the Fluent for Mac launch videos, all synthesised here (numpy/scipy, no samples).

For each video (reel, tutorial) it writes
  <video>_sfx.wav    sound effects only (stamps, pen strokes, clicks, pops) on the 115 BPM grid, for the
                     version where a song is added from Instagram's library ("Uptown Funk", 115 BPM);
  <video>_music.wav  an original 115 BPM disco-funk track (E minor: Em9 A9 Em9 Bm7) with the same
                     effects, mastered to about -14 LUFS.
"""
import os
import numpy as np
import pyloudnorm as pyln
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

from timeline import BAR, BEAT, REEL, REEL_BARS, STEPS, TUTORIAL_BARS, b

HERE = os.path.dirname(os.path.abspath(__file__))
SR = 48000
rng = np.random.default_rng(11)


def filt(x, kind, fc, order=2):
    return sosfilt(butter(order, fc, kind, fs=SR, output="sos"), x)


def place(buf, sig, start, gain=1.0):
    i = int(round(start * SR))
    if i >= len(buf) or i < 0:
        return
    j = min(len(buf), i + len(sig))
    buf[i:j] += gain * sig[: j - i]


def env(n, attack=0.002, decay=0.1):
    t = np.arange(n) / SR
    return np.minimum(1, t / max(attack, 1e-4)) * np.exp(-t / decay)


def hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def saw(f, n, detune=0.0):
    t = np.arange(n) / SR
    return 2 * ((f * (1 + detune) * t) % 1.0) - 1


# ---------------------------------------------------------------- drums and instruments

def kick():
    n = int(0.32 * SR)
    t = np.arange(n) / SR
    return np.sin(2 * np.pi * (48 + 110 * np.exp(-t * 38)) * t) * np.exp(-t / 0.13)


def clap():
    n = int(0.25 * SR)
    noise = filt(rng.standard_normal(n), "band", [900, 5200])
    e = np.zeros(n)
    for k in range(3):
        e += np.roll(env(n, 0.0005, 0.01), int(k * 0.01 * SR))
    return noise * (e + 0.55 * env(n, 0.001, 0.085))


def hat(open_=False):
    n = int((0.2 if open_ else 0.045) * SR)
    return filt(rng.standard_normal(n), "high", 7500) * env(n, 0.0005, 0.07 if open_ else 0.011)


def bass(m, length):
    n = int(length * SR)
    t = np.arange(n) / SR
    s = saw(hz(m), n) * 0.5 + np.sin(2 * np.pi * hz(m) * t) * 0.8
    body = filt(s, "low", 420) * env(n, 0.002, length * 0.6)
    pluck = filt(s, "band", [600, 2400]) * env(n, 0.001, 0.025) * 0.5     # bright attack only
    return body + pluck


def stab(chord, length=0.16):
    n = int(length * SR)
    out = np.zeros(n)
    for m in chord:
        for d in (-0.003, 0.004):
            out += saw(hz(m), n, d)
    return filt(out, "band", [400, 3200]) * env(n, 0.002, 0.07) * 0.09


def pad(chord, length):
    n = int(length * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for m in chord:
        out += np.sin(2 * np.pi * hz(m) * t) + 0.3 * np.sin(2 * np.pi * 2 * hz(m) * t)
    shape = np.minimum(1, t / 0.2) * np.clip((length - t) / 0.3, 0, 1)
    return out * shape * 0.035


def bell(m, length=0.5):
    n = int(length * SR)
    t = np.arange(n) / SR
    f = hz(m)
    return (np.sin(2 * np.pi * f * t) + 0.4 * np.sin(2 * np.pi * 2.76 * f * t) * np.exp(-t / 0.08)) * np.exp(-t / 0.25) * 0.22


CHORDS = [[52, 55, 59, 62, 66], [57, 61, 64, 67, 71], [52, 55, 59, 62, 66], [59, 62, 66, 69]]   # Em9 A9 Em9 Bm7
ROOTS = [28, 33, 28, 35]
STAB_16THS = [2, 6, 7, 10, 14]
LEAD = [76, 79, 81, 83, 81, 79, 76, 74]          # a little bell hook, one note per beat over two bars


def build_music(bars, drop_bar, lead_bars, end_bar):
    N = int(SR * bars * BAR)
    drums, low, keys, lead = (np.zeros(N) for _ in range(4))
    for bar in range(bars):
        ci = bar % 4
        full = drop_bar <= bar < end_bar
        if bar >= end_bar:                              # outro: one held chord, one kick
            place(keys, pad(CHORDS[0] + [64], BAR * (bars - bar)), b(bar), 1.4)
            place(low, bass(ROOTS[0] + 12, BAR), b(bar), 0.9)
            place(drums, kick(), b(bar), 1.0)
            place(lead, bell(88, 1.2), b(bar), 0.8)
            break
        place(keys, pad(CHORDS[ci], BAR + 0.05), b(bar), 1.0)
        for e in range(8):                              # disco octave bass
            m = ROOTS[ci] + (12 if e % 2 else 0)
            place(low, bass(m, BEAT / 2 * 0.9), b(bar, e / 2), 0.9 if full else 0.55)
        if full:
            for k in range(4):
                place(drums, kick(), b(bar, k), 1.0)
                if k in (1, 3):
                    place(drums, clap(), b(bar, k), 0.6)
                place(drums, hat(open_=True), b(bar, k + 0.5), 0.22)
            for s16 in range(16):
                place(drums, hat(), b(bar, s16 / 4), 0.16 if s16 % 2 else 0.1)
            for s16 in STAB_16THS:
                place(keys, stab(CHORDS[ci]), b(bar, s16 / 4), 1.0)
        else:
            place(drums, kick(), b(bar), 0.8)
            place(drums, kick(), b(bar, 2), 0.6)
            for e in range(8):
                place(drums, hat(), b(bar, e / 2), 0.1)
        if bar in lead_bars:
            for k in range(4):
                place(lead, bell(LEAD[(bar % 2) * 4 + k]), b(bar, k), 1.0)
    mix = drums + low + keys + lead
    intro_end = int(drop_bar * BAR * SR)
    mix[:intro_end] = filt(mix, "low", 1000)[:intro_end] * 1.35   # heard through a wall until the drop
    left = mix + 0.3 * np.roll(keys + lead, int(0.011 * SR))
    right = mix + 0.3 * np.roll(keys + lead, int(0.017 * SR))
    fade = np.ones(N)
    tail = int(0.4 * SR)
    fade[-tail:] = np.linspace(1, 0, tail)
    return np.stack([left * fade, right * fade], axis=1)


# ---------------------------------------------------------------- sound effects

def whoosh(length=0.4):
    n = int(length * SR)
    noise = rng.standard_normal(n)
    out = np.zeros(n)
    for k in range(20):
        a, z = k * n // 20, (k + 1) * n // 20
        lo = 300 + 4500 * k / 20
        out[a:z] = filt(noise[a:z], "band", [lo, lo * 1.8])
    return out * np.sin(np.pi * np.linspace(0, 1, n)) ** 1.5 * 0.8


def thump():                  # a rubber stamp hitting paper on a desk
    n = int(0.5 * SR)
    t = np.arange(n) / SR
    body = np.sin(2 * np.pi * (70 + 90 * np.exp(-t * 30)) * t) * np.exp(-t / 0.12)
    slap = filt(rng.standard_normal(n), "band", [400, 4000]) * env(n, 0.0005, 0.03)
    return body + 0.7 * slap


def pen(length=0.18):         # felt pen dragged across paper
    n = int(length * SR)
    grain = filt(rng.standard_normal(n), "band", [1800, 6500]) * (0.6 + 0.4 * np.sin(np.linspace(0, 30, n)))
    return grain * np.sin(np.pi * np.linspace(0, 1, n)) * 0.5


def click():                  # trackpad / mouse click
    n = int(0.04 * SR)
    return filt(rng.standard_normal(n), "band", [2000, 8000]) * env(n, 0.0003, 0.004) + \
        np.sin(2 * np.pi * 1400 * np.arange(n) / SR) * env(n, 0.0005, 0.006) * 0.4


def key():
    n = int(0.05 * SR)
    return filt(rng.standard_normal(n), "band", [1500, 7000]) * env(n, 0.0005, 0.008) * 0.8


def pop_(m=84):
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    f = hz(m) * (1 + 0.6 * np.exp(-t * 60))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env(n, 0.001, 0.03) * 0.5


def chime(notes=(83, 88, 95), length=1.2):
    n = int(length * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for k, m in enumerate(notes):
        s = (np.sin(2 * np.pi * hz(m) * t) + 0.3 * np.sin(2 * np.pi * 2 * hz(m) * t)) * np.exp(-t / 0.35)
        out += np.concatenate([np.zeros(int(k * 0.07 * SR)), s])[:n] * 0.45
    return out


def reel_sfx():
    S = REEL
    s = np.zeros(int(SR * REEL_BARS * BAR))
    at = lambda scene, beats: S[scene] + beats * BEAT
    for bt, g in [(0, 0.5), (0.5, 0.6), (2, 1.0)]:
        place(s, pop_(80 + 4 * bt), at("hook", bt), g)
    place(s, pen(0.3), at("hook", 2.6), 0.6)
    place(s, whoosh(), S["stamp"] - 0.2, 0.6)
    place(s, thump(), at("stamp", 2), 1.0)
    place(s, whoosh(), S["proof"] - 0.2, 0.5)
    for i in range(3):
        place(s, pen(), at("proof", 0.8 + i * 0.9), 0.8)
    for i in range(30):                     # "I think we should meet at six." typed over 1.6 beats
        place(s, key(), at("proof", 3.6) + i * 1.6 * BEAT / 30, 0.45)
    place(s, whoosh(), S["real"] - 0.2, 0.5)
    place(s, click(), at("real", 1.0), 0.8)
    place(s, pop_(88), at("real", 2), 0.6)
    place(s, chime((88, 95), 0.8), at("real", 4), 0.5)
    for i in range(6):
        place(s, pop_(76 + 2 * i), at("notes", 0.6 + i * 0.5), 0.45)
    place(s, whoosh(), S["get"] - 0.2, 0.5)
    place(s, click(), at("get", 1.4), 0.9)
    place(s, click(), at("get", 4.0), 0.9)
    place(s, whoosh(0.5), S["end"] - 0.25, 0.6)
    place(s, thump(), at("end", 1), 1.0)
    place(s, pop_(90), at("end", 3), 0.5)
    place(s, chime(), at("end", 5), 0.5)
    return s


def tutorial_sfx():
    s = np.zeros(int(SR * TUTORIAL_BARS * BAR))
    for st in STEPS:
        t0 = st["bar"] * BAR
        place(s, whoosh(0.35), t0 - 0.15, 0.35)
        if st["id"] in ("intro", "outro"):
            place(s, chime() if st["id"] == "outro" else pop_(84), t0 + (BEAT if st["id"] == "outro" else 0.05), 0.5)
            continue
        place(s, pop_(86), t0 + 0.02, 0.35)
        for c in st.get("clicks", []):
            place(s, click(), t0 + c[0] * BEAT, 0.9)
        if "drag" in st:
            d = st["drag"]
            place(s, click(), t0 + d["at"] * BEAT, 0.9)
            place(s, click(), t0 + (d["at"] + d["dur"]) * BEAT, 0.9)
            place(s, pop_(88), t0 + (d["at"] + d["dur"]) * BEAT + 0.05, 0.4)
        if "typing" in st:
            place(s, key(), t0 + st["typing"]["at"] * BEAT, 0.7)
            place(s, key(), t0 + st["typing"]["at"] * BEAT + 0.05, 0.6)
    return s


def master(stereo, target_lufs):
    meter = pyln.Meter(SR)
    for _ in range(4):
        stereo = stereo * 10 ** ((target_lufs - meter.integrated_loudness(stereo)) / 20)
        ceiling = 10 ** (-2.5 / 20)
        stereo = np.tanh(stereo / ceiling) * ceiling
    return stereo, meter.integrated_loudness(stereo)


def write(name, stereo):
    wavfile.write(os.path.join(HERE, name), SR, (np.clip(stereo, -1, 1) * 32767).astype(np.int16))


if __name__ == "__main__":
    jobs = {
        "reel": (reel_sfx(), build_music(REEL_BARS, drop_bar=1, lead_bars={3, 4, 7, 8}, end_bar=9), 0.55),
        "tutorial": (tutorial_sfx(), build_music(TUTORIAL_BARS, drop_bar=1, lead_bars={0, 17, 18}, end_bar=19), 0.5),
    }
    for name, (sfx, music, mgain) in jobs.items():
        st = np.stack([sfx, sfx], axis=1)
        sfx_m, l1 = master(st, -16.0)
        write(f"{name}_sfx.wav", sfx_m)
        mix_m, l2 = master(music * mgain + st * 0.6, -14.0)
        write(f"{name}_music.wav", mix_m)
        print(f"{name}: sfx {l1:.1f} LUFS, music {l2:.1f} LUFS, {len(sfx) / SR:.2f} s")
