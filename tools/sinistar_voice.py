#!/usr/bin/env python3
"""Gives a clean recording the Sinistar's voice: deep, slow and gritty.

The cabinet spoke through an HC-55516 CVSD chip: one bit per sample, a step size that grows and shrinks
with the signal, and a lot of hiss and grit as a result. This pitches the voice down (by playing it
slower), runs it through a simple CVSD encode and decode, band-limits it like a small cabinet speaker
and writes it at the game's 22050 Hz.
Usage: sinistar_voice.py in.wav out.wav [pitch]
"""
import math
import struct
import sys
import wave

src, dst = sys.argv[1], sys.argv[2]
pitch = float(sys.argv[3]) if len(sys.argv) > 3 else 0.74

with wave.open(src, "rb") as w:
    rate = w.getframerate()
    raw = w.readframes(w.getnframes())
samples = [s / 32768 for s in struct.unpack("<%dh" % (len(raw) // 2), raw)]

OUT_RATE = 22050
CVSD_RATE = 16000

# Pitch down by stretching: each output sample reads further back in the input.
def resample(data, ratio):
    out = []
    n = int(len(data) / ratio)
    for k in range(n):
        x = k * ratio
        i = int(x)
        f = x - i
        a = data[i]
        b = data[i + 1] if i + 1 < len(data) else a
        out.append(a + (b - a) * f)
    return out

# Down to the chip's bit rate (pitched down at the same time), through CVSD, then up to the game's rate.
chip = resample(samples, rate * pitch / CVSD_RATE)

# CVSD: the decoder's estimate climbs or falls by a step each bit; three bits the same way in a row
# grow the step (syllabic companding), otherwise it shrinks. Leaky integration keeps it centred.
estimate, step, history, decoded = 0.0, 0.01, [0, 0, 0], []
peak_in = max(1e-9, max(abs(s) for s in chip))
for s in chip:
    bit = 1 if s / peak_in > estimate else 0
    history = history[1:] + [bit]
    if history[0] == history[1] == history[2]:
        step = min(step * 1.6 + 0.004, 0.25)
    else:
        step = max(step * 0.85, 0.004)
    estimate = estimate * 0.985 + (step if bit else -step)
    decoded.append(estimate)

# A one-pole low-pass at the chip's output, and a gentle high-pass to lose the rumble.
lp, hp, prev, filtered = 0.0, 0.0, 0.0, []
for s in decoded:
    lp += 0.35 * (s - lp)
    hp = 0.98 * (hp + lp - prev)
    prev = lp
    filtered.append(hp)

out = resample(filtered, CVSD_RATE / OUT_RATE)

# Trim the silence at either end.
peak = max(1e-9, max(abs(s) for s in out))
start = next((i for i, s in enumerate(out) if abs(s) > peak * 0.03), 0)
end = len(out) - next((i for i, s in enumerate(reversed(out)) if abs(s) > peak * 0.03), 0)
out = out[max(0, start - 200):min(len(out), end + 600)]

with wave.open(dst, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(OUT_RATE)
    w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s / peak * 0.9)) * 32767)) for s in out))
