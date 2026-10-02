"""Generate Lighting's ASMR footsteps with only the Python standard library.
Run from anywhere: python3 Tools/audio/build_asmr_steps.py
Writes 8 stereo 44.1 kHz PCM16 clips, soft_step_01..08.wav (odd = left foot, even = right foot),
into Assets/LightingBlocks/Resources/Audio/LightingSteps, plus a 12-step walk preview into
Tools/audio/preview/asmr_walk.wav. Deterministic: fixed seeds, so a rebuild writes the same files.

Each step is dry and close-mic (no reverb, that is what reads as ASMR), in four layers:
a soft heel pat, a crisp crystal tick with a faint glassy ring, fine crystal grit spread
across the stereo field, and a smaller toe roll after the heel.
"""
from pathlib import Path
import math, random, struct, wave

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/LightingBlocks/Resources/Audio/LightingSteps"
PREVIEW = ROOT / "Tools/audio/preview"
RATE = 44100
LENGTH = int(.28 * RATE)
PEAK = .30
FOOT_PAN = .2            # left foot -0.2, right foot +0.2
EAR_DELAY = 9            # samples (~0.2 ms) the far ear hears the foot later


def highpass(signal, cutoff, passes=2):
    """One-pole high-pass, run `passes` times for a steeper slope."""
    rc = 1 / (2 * math.pi * cutoff); alpha = rc / (rc + 1 / RATE)
    for _ in range(passes):
        out, y, previous = [], 0.0, 0.0
        for x in signal:
            y = alpha * (y + x - previous); previous = x; out.append(y)
        signal = out
    return signal


def lowpass(signal, cutoff):
    alpha = (1 / RATE) / (1 / (2 * math.pi * cutoff) + 1 / RATE); y = 0.0; out = []
    for x in signal:
        y += alpha * (x - y); out.append(y)
    return out


def add(track, signal, start):
    for i, value in enumerate(signal):
        if 0 <= start + i < len(track): track[start + i] += value


def pat(rng, top, bottom, decay, amp):
    """A soft, cushioned thump: a falling sine with a breath of low noise under it."""
    n = int(decay * 6 * RATE); phase = 0.0; out = []
    body = lowpass([rng.uniform(-1, 1) for _ in range(n)], 380)
    for i in range(n):
        t = i / RATE
        freq = bottom + (top - bottom) * math.exp(-t / .012)
        phase += 2 * math.pi * freq / RATE
        envelope = (1 - math.exp(-t / .0015)) * math.exp(-t / decay)
        out.append(amp * envelope * (math.sin(phase) + 1.6 * body[i]))
    return out


def tick(rng, amp, glass):
    """A crisp high click and a faint glassy partial: the tingle."""
    n = int(.05 * RATE)
    click = highpass([rng.uniform(-1, 1) * math.exp(-i / (RATE * .0012)) for i in range(n)], 3000)
    g1, g2 = glass, glass * 2.76
    return [amp * click[i] + amp * .18 * math.exp(-(i / RATE) / .04) * math.sin(2 * math.pi * g1 * i / RATE)
            + amp * .07 * math.exp(-(i / RATE) / .02) * math.sin(2 * math.pi * g2 * i / RATE + .6) for i in range(n)]


def grain(rng, amp):
    n = int(rng.uniform(.0015, .004) * RATE)
    return highpass([amp * rng.uniform(-1, 1) * math.exp(-i / (RATE * .0008)) for i in range(n)], rng.uniform(4000, 6000))


def place_panned(left, right, signal, start, pan):
    """Equal-power pan; the far ear gets the sound EAR_DELAY samples later."""
    angle = (pan + 1) * math.pi / 4
    gain_left, gain_right = math.cos(angle), math.sin(angle)
    add(left, [v * gain_left for v in signal], start + (EAR_DELAY if pan > 0 else 0))
    add(right, [v * gain_right for v in signal], start + (EAR_DELAY if pan < 0 else 0))


def step(variant):
    rng = random.Random(4200 + variant)
    pan = -FOOT_PAN if variant % 2 else FOOT_PAN
    left, right = [0.0] * LENGTH, [0.0] * LENGTH
    # 1 heel pat, 2 crystal tick on the heel
    place_panned(left, right, pat(rng, rng.uniform(170, 190), rng.uniform(112, 128), rng.uniform(.024, .030), .55), 0, pan)
    place_panned(left, right, tick(rng, .35, rng.uniform(2600, 4000)), int(.002 * RATE), pan)
    # 3 fine crystal grit, each grain somewhere in the stereo field, thicker early
    for _ in range(rng.randint(6, 14)):
        at = int((.015 + .075 * rng.random() ** 1.6) * RATE)
        place_panned(left, right, grain(rng, rng.uniform(.04, .12)), at, max(-.6, min(.6, pan + rng.uniform(-.4, .4))))
    # 4 toe roll: a smaller pat, tick and a few grains after the heel
    toe = int(rng.uniform(.055, .085) * RATE)
    place_panned(left, right, pat(rng, rng.uniform(140, 160), rng.uniform(100, 116), rng.uniform(.016, .020), .25), toe, pan)
    place_panned(left, right, tick(rng, .15, rng.uniform(3000, 4400)), toe + int(.001 * RATE), pan)
    for _ in range(rng.randint(3, 5)):
        place_panned(left, right, grain(rng, rng.uniform(.03, .08)), toe + int(rng.uniform(.004, .03) * RATE), pan + rng.uniform(-.3, .3))
    # 1 ms fade in, 8 ms fade out, normalized to PEAK
    fade_in, fade_out = int(.001 * RATE), int(.008 * RATE)
    for i in range(LENGTH):
        gain = min(1.0, i / fade_in, (LENGTH - 1 - i) / fade_out)
        left[i] *= gain; right[i] *= gain
    scale = PEAK / max(max(abs(v) for v in left), max(abs(v) for v in right), 1e-9)
    return [v * scale for v in left], [v * scale for v in right]


def write(path, left, right):
    frames = b"".join(struct.pack("<hh", max(-32768, min(32767, round(l * 32767))), max(-32768, min(32767, round(r * 32767))))
                      for l, r in zip(left, right))
    with wave.open(str(path), "wb") as wav:
        wav.setparams((2, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(frames)


OUT.mkdir(parents=True, exist_ok=True)
steps = []
for variant in range(1, 9):
    left, right = step(variant)
    steps.append((left, right))
    write(OUT / f"soft_step_{variant:02}.wav", left, right)
    print(f"soft_step_{variant:02}.wav: {LENGTH / RATE:.2f}s, peak {PEAK}, {'left' if variant % 2 else 'right'} foot")

# Preview: a 12-step walk at about 2.8 steps a second, feet alternating as LightingFootsteps plays them.
PREVIEW.mkdir(parents=True, exist_ok=True)
spacing = int(.36 * RATE); walk_left, walk_right = [0.0] * (spacing * 12 + LENGTH), [0.0] * (spacing * 12 + LENGTH)
for k in range(12):
    left, right = steps[(k * 3) % 8]
    add(walk_left, left, k * spacing); add(walk_right, right, k * spacing)
write(PREVIEW / "asmr_walk.wav", walk_left, walk_right)
print("preview: Tools/audio/preview/asmr_walk.wav")
