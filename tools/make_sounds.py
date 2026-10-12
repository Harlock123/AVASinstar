#!/usr/bin/env python3
"""Synthesizes the game's sound effects into ../Sounds as 16-bit mono WAV files.

Nothing is sampled from the original machine: every effect is built here from oscillators and noise,
so the sounds can be tweaked and regenerated. Run it from anywhere: python3 tools/make_sounds.py
(The Sinistar's words come from tools/make_voice.sh; its roar and scream are made here.)
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds")
random.seed(1982)


def write(name, samples, volume=0.6):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def n(seconds):
    return int(RATE * seconds)


def env(i, total, attack=0.005, release=1.0):
    """Linear attack, then a decay shaped by `release` (higher = faster fade)."""
    t = i / RATE
    a = min(1.0, t / attack) if attack > 0 else 1.0
    return a * (1 - i / total) ** release


def sweep(seconds, f0, f1, wave_fn, release=1.0, attack=0.005):
    total, phase, out = n(seconds), 0.0, []
    for i in range(total):
        f = f0 + (f1 - f0) * i / total
        phase += f / RATE
        out.append(wave_fn(phase) * env(i, total, attack=attack, release=release))
    return out


def square(p):
    return 1.0 if (p % 1) < 0.5 else -1.0


def tri(p):
    return 4 * abs((p % 1) - 0.5) - 1


def saw(p):
    return 2 * (p % 1) - 1


def sine(p):
    return math.sin(2 * math.pi * p)


def noise(seconds, release=1.0, smooth=0.0, attack=0.002):
    total, last, out = n(seconds), 0.0, []
    for i in range(total):
        last = last * smooth + random.uniform(-1, 1) * (1 - smooth)
        out.append(last * env(i, total, attack=attack, release=release))
    return out


def mix(*tracks):
    length = max(len(t) for t in tracks)
    return [sum(t[i] for t in tracks if i < len(t)) for i in range(length)]


def scale(track, k):
    return [s * k for s in track]


def silence(seconds):
    return [0.0] * n(seconds)


os.makedirs(OUT, exist_ok=True)

# The ship's gun: a quick, bright zap.
write("Fire.wav", sweep(0.09, 1800, 500, square, release=1.4), 0.22)

# A shot striking a planetoid: a dull tick.
write("RockHit.wav", mix(noise(0.05, release=3, smooth=0.6), sweep(0.05, 300, 200, tri, release=2)), 0.3)

# A crystal knocked loose: a glassy chime.
total = n(0.35)
chime = []
for i in range(total):
    t = i / RATE
    chime.append((math.sin(2 * math.pi * 1760 * t) + 0.6 * math.sin(2 * math.pi * 2637 * t)) * math.exp(-t * 9))
write("Crystal.wav", chime, 0.3)

# A crystal collected: a rising blip.
write("Pickup.wav", sweep(0.12, 900, 1800, tri, release=0.6), 0.35)

# A Sinibomb launched: a swooping whoosh.
write("BombLaunch.wav", mix(sweep(0.5, 200, 900, saw, release=1, attack=0.03), scale(noise(0.5, release=1.5, smooth=0.5), 0.5)), 0.4)

# A Sinibomb striking the Sinistar: a crunching blast.
write("BombHit.wav", mix(noise(0.9, release=1.6, smooth=0.55), scale(sweep(0.9, 180, 40, square, release=1.6), 0.6)), 0.75)

write("WorkerBoom.wav", mix(noise(0.3, release=2.5, smooth=0.3), sweep(0.3, 700, 150, square, release=2)), 0.4)
write("WarriorBoom.wav", mix(noise(0.7, release=2, smooth=0.55), scale(sweep(0.7, 300, 60, tri, release=1.6), 0.7)), 0.6)

# A warrior's shot: a nasal buzz.
write("WarriorFire.wav", sweep(0.16, 600, 380, square, release=1.2), 0.22)

write("ShipBoom.wav", mix(noise(1.4, release=1.6, smooth=0.6), scale(sweep(1.4, 140, 30, saw, release=1.5), 0.5)), 0.85)

# A piece added to the Sinistar: a heavy metallic clunk.
total = n(0.4)
clunk = []
for i in range(total):
    t = i / RATE
    clunk.append((math.sin(2 * math.pi * 110 * t) + 0.7 * math.sin(2 * math.pi * 173 * t) + 0.4 * math.sin(2 * math.pi * 291 * t)) * math.exp(-t * 10))
write("Piece.wav", mix(clunk, scale(noise(0.06, release=3, smooth=0.5), 0.6)), 0.5)

# The Sinistar's roar: a huge, rough growl that swells and falls, rough with fast pulses like a beast's
# throat. (The cabinet's roar was a processed gorilla recording; this one is made from scratch.)
total = n(1.8)
roar, phase, last = [], 0.0, 0.0
for i in range(total):
    t = i / total
    f = 70 + 60 * math.sin(math.pi * min(1, t * 1.4)) - 25 * t
    phase += f / RATE
    last = last * 0.75 + random.uniform(-1, 1) * 0.25
    pulse = 0.55 + 0.45 * math.sin(2 * math.pi * 23 * i / RATE)
    shape = math.sin(math.pi * min(1, t * 1.1)) ** 0.7
    roar.append((saw(phase) * 0.8 + square(phase * 2) * 0.25 + last * 1.4) * pulse * shape)
write("Roar.wav", roar, 0.85)

# Its death scream: a long, wavering wail that falls away.
total = n(2.2)
scream, phase, last = [], 0.0, 0.0
for i in range(total):
    t = i / total
    f = 700 * (0.3 ** t) + 30 * math.sin(2 * math.pi * 9 * i / RATE)
    phase += f / RATE
    last = last * 0.6 + random.uniform(-1, 1) * 0.4
    scream.append((saw(phase) * 0.7 + tri(phase * 1.5) * 0.3 + last * 0.35) * env(i, total, attack=0.04, release=1.2))
write("VoiceScream.wav", scream, 0.8)

# The Sinistar blowing apart.
total = n(3.5)
blast, last, deep = [], 0.0, 0.0
for i in range(total):
    t = i / RATE
    last = last * 0.8 + random.uniform(-1, 1) * 0.2
    deep = deep * 0.97 + random.uniform(-1, 1) * 0.03
    shape = min(1, t / 0.04) * math.exp(-t * 0.9)
    blast.append((0.6 * last + 5 * deep + 0.5 * math.sin(2 * math.pi * 40 * t)) * shape)
write("SinistarBoom.wav", blast, 0.9)

# A new zone: a sweep and a chord.
zone = sweep(0.5, 200, 800, tri, release=0.5, attack=0.05)
chord = mix(sweep(0.6, 523, 523, square, release=0.8), scale(sweep(0.6, 659, 659, square, release=0.8), 0.8),
            scale(sweep(0.6, 784, 784, square, release=0.8), 0.7))
write("Zone.wav", zone + chord, 0.3)

notes = []
for f in (523, 659, 784, 1047, 784, 1047):
    notes += sweep(0.08, f, f, square, release=0.3)
write("ExtraShip.wav", notes, 0.3)

write("Empty.wav", sweep(0.04, 200, 150, square, release=2) + silence(0.05) + sweep(0.04, 200, 150, square, release=2), 0.3)

# Thrust: a soft rush, flat so it loops.
total = n(0.6)
rush, last = [], 0.0
for i in range(total):
    last = last * 0.85 + random.uniform(-1, 1) * 0.15
    rush.append(last)
write("Thrust.wav", rush, 0.25)

# The Sinistar's heartbeat once it lives: lub-dub, lub-dub, low and heavy, one beat per loop.
total = n(0.8)
beat = []
for i in range(total):
    t = i / RATE
    lub = math.sin(2 * math.pi * 55 * t) * math.exp(-t * 14) if t < 0.3 else 0
    t2 = t - 0.22
    dub = 0.7 * math.sin(2 * math.pi * 48 * t2) * math.exp(-t2 * 16) if t2 > 0 else 0
    beat.append(lub + dub)
write("Heartbeat.wav", beat, 0.7)

print("Wrote sounds to", os.path.normpath(OUT))
