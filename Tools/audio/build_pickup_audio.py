"""Generate four original layered crystal-pickup cues with only the Python standard library.
Run from anywhere: python Tools/audio/build_pickup_audio.py
44.1 kHz mono PCM16; deterministic variants normalized to a safe .36 peak.
Each clip combines a soft resonant impact, a two-part crystalline note and an airy shimmer.
"""
from pathlib import Path
import math, random, struct, wave

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/LightingBlocks/Resources/Audio/LightingPickups"
OUT.mkdir(parents=True, exist_ok=True)
DURATIONS = (.44, .50, .58, .66)
SEMITONES = (0, 2, 4, 7)
RATE = 44100

for variant, (duration, semitone) in enumerate(zip(DURATIONS, SEMITONES), 1):
    rng = random.Random(1973 + variant)
    count = int(duration * RATE)
    samples = []
    previous_noise = 0.0
    fundamental = 690.0 * 2 ** (semitone / 12)
    for n in range(count):
        t = n / RATE
        attack = 1 - math.exp(-t / .0025)
        tail = min(1.0, max(0.0, (duration - t) / .035)) ** 2

        # Quiet wooden-glass tap with a fast downward pitch bend.
        impact = .24 * math.sin(2 * math.pi * (175 * t - 125 * t * t)) * math.exp(-t / .021)
        impact += .12 * math.sin(2 * math.pi * 310 * t) * math.exp(-t / .012)

        # A clear bell note and a soft, slightly inharmonic upper partial.
        note_env = math.exp(-t / (.15 + variant * .008))
        note = .34 * math.sin(2 * math.pi * fundamental * t) * note_env
        note += .105 * math.sin(2 * math.pi * fundamental * 2.006 * t + .2) * math.exp(-t / .105)
        note += .035 * math.sin(2 * math.pi * fundamental * 3.01 * t) * math.exp(-t / .072)

        # Two airy, high partials bloom after the impact and leave a soft tail.
        u = max(0.0, t - .055)
        shimmer_gate = 1 - math.exp(-u / .018)
        shimmer = shimmer_gate * math.exp(-u / (.20 + variant * .012))
        shimmer *= .065 * math.sin(2 * math.pi * (1620 * u + 360 * u * u))
        shimmer += shimmer_gate * math.exp(-u / .17) * .035 * math.sin(2 * math.pi * (2430 * u + 620 * u * u) + .7)

        # A restrained breath of deterministic filtered noise softens the onset and tail.
        white = rng.random() * 2 - 1
        airy = white - previous_noise
        previous_noise = white
        air_env = math.exp(-max(0.0, t - .035) / .16) * (1 - math.exp(-t / .009))
        noise = .014 * airy * air_env

        samples.append((impact + note + shimmer + noise) * attack * tail)

    peak = max(abs(value) for value in samples)
    scale = .36 / max(peak, 1e-9)
    pcm = b"".join(struct.pack("<h", max(-32768, min(32767, round(value * scale * 32767)))) for value in samples)
    path = OUT / f"pickup_{variant:02}.wav"
    with wave.open(str(path), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(pcm)
    print(f"{path.name}: {duration:.3f}s, peak .36, note +{semitone} semitones")
