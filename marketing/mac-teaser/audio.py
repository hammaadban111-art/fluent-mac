"""Soundtrack for the Fluent for Mac teaser, all synthesised here (numpy/scipy, no samples).

Writes:
  sfx.wav    sound effects only (whooshes, key clicks, the bubble click, chimes), on the 171 BPM
             grid, for the version where the song is added from Instagram's music library;
  music.wav  an original 171 BPM synth-pop track (A minor: Am F C G) with the same effects,
             mastered to about -14 LUFS.
"""
import os
import numpy as np
import pyloudnorm as pyln
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

from timeline import BEAT, BAR, DURATION, SCENES, b

HERE = os.path.dirname(os.path.abspath(__file__))
SR = 48000
N = int(SR * DURATION)
rng = np.random.default_rng(7)


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


# ---------------------------------------------------------------- sound effects

def key_click(bright=1.0):
    n = int(0.05 * SR)
    noise = rng.standard_normal(n)
    click = filt(noise, "band", [1800 * bright, 7000], 2) * env(n, 0.0005, 0.008)
    body = np.sin(2 * np.pi * (180 + 60 * rng.random()) * np.arange(n) / SR) * env(n, 0.001, 0.012) * 0.5
    return click + body


def thock():
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    return (np.sin(2 * np.pi * 95 * t) * env(n, 0.001, 0.035) +
            0.5 * filt(rng.standard_normal(n), "band", [600, 3000]) * env(n, 0.0005, 0.01))


def mouse_click():
    n = int(0.04 * SR)
    return filt(rng.standard_normal(n), "band", [2500, 9000]) * env(n, 0.0002, 0.004) * 1.2


def whoosh(length=0.45, up=True):
    n = int(length * SR)
    t = np.linspace(0, 1, n)
    noise = rng.standard_normal(n)
    out = np.zeros(n)
    # sweep a band-pass across the noise in slices
    slices = 24
    for k in range(slices):
        a, z = k * n // slices, (k + 1) * n // slices
        f = (k / slices) if up else 1 - k / slices
        lo = 300 + 5000 * f
        out[a:z] = filt(noise[a:z], "band", [lo, lo * 1.8])
    shape = np.sin(np.pi * t) ** 1.5
    return out * shape * 0.9


def riser(length):
    n = int(length * SR)
    t = np.arange(n) / SR
    f = 200 * (1 + 12 * (t / length) ** 2)
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR) * 0.25
    noise = filt(rng.standard_normal(n), "high", 1500) * (t / length) ** 2 * 0.6
    return (tone + noise) * (t / length) ** 1.5


def impact():
    n = int(1.6 * SR)
    t = np.arange(n) / SR
    sub = np.sin(2 * np.pi * (55 + 60 * np.exp(-t * 18)) * t) * np.exp(-t / 0.5)
    crash = filt(rng.standard_normal(n), "high", 3000) * np.exp(-t / 0.45) * 0.35
    return sub + crash


def chime(notes=(84, 91), length=0.9):
    n = int(length * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for k, m in enumerate(notes):
        f = hz(m)
        s = (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * 2 * f * t)) * np.exp(-t / 0.3)
        out += np.concatenate([np.zeros(int(k * 0.06 * SR)), s])[:n] * 0.5
    return out


def blip(m=88):
    n = int(0.08 * SR)
    t = np.arange(n) / SR
    return np.sin(2 * np.pi * hz(m) * t) * env(n, 0.001, 0.02) * 0.4


def build_sfx():
    s = np.zeros(N)
    # Hook: a word per beat, each landing with a heavy key.
    for k in range(4):
        place(s, thock(), b(0, k), 0.9)
        place(s, key_click(), b(0, k) + 0.01, 0.6)
    # Slow, uneven typing.
    t = SCENES["slow"] + 0.05
    while t < SCENES["faster"] - 0.15:
        place(s, key_click(0.8 + 0.4 * rng.random()), t, 0.55)
        t += 0.12 + 0.16 * rng.random()
    place(s, thock(), SCENES["faster"] - 0.3, 0.5)          # backspace
    place(s, whoosh(0.4), SCENES["faster"] - 0.2, 0.7)
    place(s, riser(BAR * 0.5), SCENES["logo"] - BAR * 0.5, 0.8)
    place(s, impact(), SCENES["logo"], 1.0)
    place(s, chime((81, 88, 93)), SCENES["logo"] + 0.05, 0.5)
    # 1. click the bubble
    place(s, whoosh(0.35), SCENES["click"] - 0.15, 0.6)
    place(s, mouse_click(), b(4, 2), 1.0)
    place(s, chime((84, 91), 0.5), b(4, 2) + 0.06, 0.35)     # recording starts
    # 2. talk: a soft blip per word
    place(s, whoosh(0.35), SCENES["talk"] - 0.15, 0.5)
    for k in range(6):
        place(s, blip(86 + (k % 3) * 2), b(5, k * 0.5 + 1), 0.35)
    place(s, whoosh(0.5, up=False), SCENES["writing"] - 0.1, 0.45)
    # 3. typed: a burst of keys and the done chime
    for k in range(10):
        place(s, key_click(1.2), SCENES["typed"] + k * 0.025, 0.35)
    place(s, chime((88, 95), 0.8), SCENES["typed"] + 0.05, 0.55)
    # hold ⌥
    place(s, thock(), SCENES["hotkey"] + BEAT, 0.9)
    place(s, key_click(0.7), b(8, 3), 0.6)
    # apps: a whoosh per card
    for k in range(4):
        place(s, whoosh(0.3), b(9, 2 * k) - 0.12, 0.55)
    place(s, whoosh(0.45), SCENES["style"] - 0.15, 0.55)
    # themes: a tick per beat
    for k in range(5):
        place(s, mouse_click(), b(12, k), 0.8)
        place(s, blip(84 + 3 * k), b(12, k), 0.25)
    place(s, riser(BAR * 0.5), SCENES["end"] - BAR * 0.5, 0.6)
    place(s, impact(), SCENES["end"], 0.9)
    place(s, chime((81, 88, 93, 100), 1.6), SCENES["end"] + 0.05, 0.45)
    return s


# ---------------------------------------------------------------- original music, 171 BPM

CHORDS = [[57, 60, 64], [53, 57, 60], [48, 52, 55], [55, 59, 62]]   # Am F C G
ROOTS = [45, 41, 36, 43]


def saw(f, n, detune=0.0):
    t = np.arange(n) / SR
    ph = (f * (1 + detune) * t) % 1.0
    return 2 * ph - 1


def pad(chord, length):
    n = int(length * SR)
    out = np.zeros(n)
    for m in chord:
        for d in (-0.004, 0.0, 0.005):
            out += saw(hz(m), n, d)
    t = np.arange(n) / SR
    shape = np.minimum(1, t / 0.08) * np.minimum(1, (length - t) / 0.15).clip(0, 1)
    return filt(out, "low", 1800) * shape * 0.06


def bass_note(m, length):
    n = int(length * SR)
    t = np.arange(n) / SR
    s = saw(hz(m), n) * 0.6 + np.sin(2 * np.pi * hz(m) * t) * 0.6
    return filt(s, "low", 700) * env(n, 0.003, length * 0.8)


def kick():
    n = int(0.35 * SR)
    t = np.arange(n) / SR
    return np.sin(2 * np.pi * (50 + 120 * np.exp(-t * 35)) * t) * np.exp(-t / 0.14)


def clap():
    n = int(0.25 * SR)
    noise = filt(rng.standard_normal(n), "band", [900, 5000])
    e = np.zeros(n)
    for k in range(3):
        e += np.roll(env(n, 0.0005, 0.012), int(k * 0.011 * SR))
    return noise * (e + 0.6 * env(n, 0.001, 0.09))


def hat(open_=False):
    n = int((0.18 if open_ else 0.05) * SR)
    return filt(rng.standard_normal(n), "high", 7000) * env(n, 0.0005, 0.06 if open_ else 0.012)


def arp_note(m, length):
    n = int(length * SR)
    t = np.arange(n) / SR
    s = np.sign(np.sin(2 * np.pi * hz(m) * t)) * 0.5 + np.sin(2 * np.pi * hz(m) * t) * 0.5
    return filt(s, "low", 3500) * env(n, 0.002, 0.07)


def build_music():
    drums = np.zeros(N)
    bass = np.zeros(N)
    keys = np.zeros(N)
    arp = np.zeros(N)
    for bar in range(15):
        ci = bar % 4
        full = 3 <= bar <= 13
        intro = bar < 3
        place(keys, pad(CHORDS[ci], BAR + 0.05), b(bar), 1.0 if not intro else 0.7)
        if bar == 14:
            place(keys, pad(CHORDS[0], BAR), b(bar), 1.1)
            place(bass, bass_note(ROOTS[0] - 12, BAR), b(bar), 1.0)
            place(drums, kick(), b(bar), 1.0)
            continue
        for e in range(8):   # bass in eighths
            if intro and bar == 2 and e >= 4:
                break
            place(bass, bass_note(ROOTS[ci] + (12 if e % 2 else 0), BEAT / 2 * 0.95), b(bar, e / 2), 0.8 if full else 0.5)
        if full:
            for k in range(4):
                if k in (0, 2):
                    place(drums, kick(), b(bar, k), 1.0)
                else:
                    place(drums, clap(), b(bar, k), 0.55)
                    place(drums, kick(), b(bar, k + 0.5), 0.45)
            for e in range(8):
                place(drums, hat(open_=(e % 2 == 1)), b(bar, e / 2), 0.25)
            chord = CHORDS[ci] + [CHORDS[ci][0] + 12]
            for s16 in range(16):
                m = chord[s16 % 4] + 12
                place(arp, arp_note(m, BEAT / 4), b(bar, s16 / 4), 0.16)
        else:
            place(drums, kick(), b(bar), 0.8)
            if bar == 1:
                place(drums, kick(), b(bar, 2), 0.6)
            for e in range(8):
                place(drums, hat(), b(bar, e / 2), 0.12)
    keys = filt(keys, "low", 1200) if False else keys
    # The intro is filtered, as if heard through a wall, and opens up on the logo.
    intro_end = int(SCENES["logo"] * SR)
    mix = drums + bass + keys + arp
    muffled = filt(mix, "low", 900)
    mix[:intro_end] = muffled[:intro_end] * 1.3
    # A little stereo width: arp and pad delayed on one side.
    left = mix + 0.25 * np.roll(arp + keys, int(0.012 * SR))
    right = mix + 0.25 * np.roll(arp + keys, int(0.019 * SR))
    fade = np.ones(N)
    tail = int(0.35 * SR)
    fade[-tail:] = np.linspace(1, 0, tail)
    return np.stack([left * fade, right * fade], axis=1)


def master(stereo, target_lufs):
    meter = pyln.Meter(SR)
    for _ in range(4):
        loud = meter.integrated_loudness(stereo)
        stereo = stereo * 10 ** ((target_lufs - loud) / 20)
        # Soft limiter: keeps sample peaks under about -1 dBFS.
        ceiling = 10 ** (-1.2 / 20)
        stereo = np.tanh(stereo / ceiling) * ceiling
    return stereo, meter.integrated_loudness(stereo)


def write(name, stereo):
    wavfile.write(os.path.join(HERE, name), SR, (np.clip(stereo, -1, 1) * 32767).astype(np.int16))


if __name__ == "__main__":
    sfx = build_sfx()
    sfx_st = np.stack([sfx, sfx], axis=1)
    sfx_m, l1 = master(sfx_st, -16.0)
    write("sfx.wav", sfx_m)
    music = build_music()
    mixed = music * 0.85 + sfx_st * 0.55
    mixed_m, l2 = master(mixed, -14.0)
    write("music.wav", mixed_m)
    print(f"sfx.wav {l1:.1f} LUFS, music.wav {l2:.1f} LUFS, {DURATION:.2f} s")
