#!/usr/bin/env python3
"""Synthesizes the game's sound effects into ../Sounds as 16-bit mono WAV files.

Nothing is sampled from the original machine: every effect is built here from oscillators and noise,
so the sounds can be tweaked and regenerated. Run it from anywhere: python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds")
random.seed(1980)


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

# A counter-missile launched: a short, rising hiss.
total = n(0.35)
hiss, last = [], 0.0
for i in range(total):
    k = 0.2 + 0.7 * i / total
    last = last * (1 - k) + random.uniform(-1, 1) * k
    hiss.append(last * env(i, total, attack=0.01, release=1.2))
write("Launch.wav", mix(hiss, scale(sweep(0.35, 300, 900, tri, release=1.2), 0.3)), 0.4)

# A fireball: the cabinet's deep, crunchy boom.
write("Boom.wav", mix(noise(0.9, release=1.8, smooth=0.55), scale(sweep(0.9, 120, 35, square, release=1.6), 0.6)), 0.6)

# A city going up: bigger, longer, rumbling.
write("CityBoom.wav", mix(noise(1.8, release=1.5, smooth=0.75), scale(sweep(1.8, 90, 25, tri, release=1.3), 1.0),
                          scale(noise(0.15, release=2, smooth=0.1), 0.6)), 0.85)

# A base destroyed: a heavy crump.
write("BaseBoom.wav", mix(noise(1.2, release=1.7, smooth=0.7), scale(sweep(1.2, 140, 30, square, release=1.5), 0.6)), 0.8)

# A bomber or satellite brought down.
write("FlyerBoom.wav", mix(noise(1.0, release=1.6, smooth=0.5), scale(sweep(1.0, 600, 80, saw, release=1.4), 0.5)), 0.65)

# Out of missiles at that base: a dull double click.
write("Empty.wav", sweep(0.04, 200, 150, square, release=2) + silence(0.05) + sweep(0.04, 200, 150, square, release=2), 0.3)

# A base down to its last three missiles: two quick warning beeps.
beep = sweep(0.08, 1400, 1400, square, release=0.3)
write("Low.wav", beep + silence(0.06) + beep, 0.2)

# The start of a wave: the cabinet's rising siren whoops, six of them.
whoop = sweep(0.22, 300, 900, square, release=0.2, attack=0.01)
alert = []
for _ in range(6):
    alert += whoop + silence(0.05)
write("Alert.wav", alert, 0.22)

# The tally: a bright tick for each missile left, a deeper chime for each city.
write("TallyMissile.wav", sweep(0.05, 1600, 1600, square, release=0.8), 0.18)
write("TallyCity.wav", mix(sweep(0.25, 523, 523, tri, release=0.8), scale(sweep(0.25, 784, 784, tri, release=0.8), 0.6)), 0.35)

# A bonus city rebuilt: a little fanfare.
notes = []
for f in (523, 659, 784, 1047, 1319):
    notes += sweep(0.09, f, f, square, release=0.3)
notes += sweep(0.35, 1568, 1568, square, release=1)
write("BonusCity.wav", notes, 0.3)

# THE END: an enormous, slow roar.
total = n(4.0)
roar, last, deep = [], 0.0, 0.0
for i in range(total):
    t = i / RATE
    last = last * 0.8 + random.uniform(-1, 1) * 0.2
    deep = deep * 0.97 + random.uniform(-1, 1) * 0.03
    shape = min(1, t / 0.05) * math.exp(-t * 0.8)
    roar.append((0.6 * last + 5 * deep + 0.5 * math.sin(2 * math.pi * 36 * t)) * shape)
write("TheEnd.wav", roar, 0.9)

# A bomber or satellite crossing: a buzzing drone, made of whole-cycle tones so it loops cleanly.
total = n(1.0)
drone = []
for i in range(total):
    t = i / RATE
    drone.append(square(110 * t) * 0.5 + square(111 * t) * 0.4 + 0.3 * math.sin(2 * math.pi * 55 * t))
write("Flyer.wav", drone, 0.12)

# A smart bomb falling: a wavering, high whine, looped.
total = n(1.0)
whine = []
for i in range(total):
    t = i / RATE
    whine.append(math.sin(2 * math.pi * 1200 * t + 6 * math.sin(2 * math.pi * 7 * t)))
write("SmartBomb.wav", whine, 0.1)

print("Wrote sounds to", os.path.normpath(OUT))
