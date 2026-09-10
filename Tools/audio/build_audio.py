#!/usr/bin/env python3
"""Generates every audio asset for Lighting Blocks.

    python3 Tools/audio/build_audio.py

Writes WAVs into Assets/Audio/, MIDI sources next to the music, and preview
files into Tools/audio/preview/ for listening checks. Fails loudly if the loop
seams or the masking budget regress.
"""

import pathlib
import sys

import numpy as np

sys.path.insert(0, str(pathlib.Path(__file__).parent))

import midi
from synth import (SR, band_rms_db, biquad, biquad_swept, butterworth_lowpass,
                   dbfs, exp_decay, fade_tail, loop_seam, noise, peak_normalize,
                   raised_cos_attack, raised_cos_release, reverb_mono,
                   reverb_stereo, rms, shaped_noise, sine, t_axis, write_wav)

ROOT = pathlib.Path(__file__).resolve().parents[2]
SFX_DIR = ROOT / "Assets" / "Resources" / "Audio" / "SFX"
MUSIC_DIR = ROOT / "Assets" / "Resources" / "Audio" / "Music"
PREVIEW_DIR = ROOT / "Tools" / "audio" / "preview"

# ---- the loop grid ----------------------------------------------------------
# 50 BPM because 2646000/50 = 52920 exactly, so beat, bar and chord boundaries
# all land on whole samples. Every musical duration below is an integer.
BPM = 50
BEAT = 52920
BAR = BEAT * 4               # 211680 = 4.8 s
LOOP = BAR * 16              # 3386880 = 76.8 s
LOOP_SECONDS = LOOP / SR     # 76.8
TAIL = 529200                # 12 s of headroom folded back into the loop

report = []


def q(freq):
    """Quantize to the loop grid so a sustained oscillator completes a whole
    number of cycles across the loop. Error is under 0.1 cent."""
    return round(freq * LOOP_SECONDS) / LOOP_SECONDS


def place(buffer, signal, start):
    # shape[-1], never len(): len() on a (2, n) stereo array returns 2.
    stop = min(start + signal.shape[-1], buffer.shape[-1])
    if stop > start:
        buffer[..., start:stop] += signal[..., :stop - start]


def pan(mono, position):
    """Equal-power pan. position -1 = hard left, +1 = hard right."""
    angle = (position + 1.0) * np.pi / 4.0
    return np.stack([mono * np.cos(angle), mono * np.sin(angle)])


def glide_phase(freqs):
    """Phase for a time-varying frequency curve."""
    return 2.0 * np.pi * np.cumsum(freqs) / SR


# =============================================================================
# 1. Footsteps - modal impact synthesis, 5 variants
# =============================================================================
# Boot and stone stay identical across variants: the grit highpass, the modal
# base frequencies and the layer amplitude ratios are frozen. Only micro-timing,
# decay rates and level move.

FOOTSTEP_LEN = 22050
STONE_MODES = (418.0, 692.0, 1131.0)   # inharmonic: 1 : 1.656 : 2.706
GRIT_HIGHPASS = 3200.0


def build_footstep(seed):
    rng = np.random.default_rng(seed)
    n = FOOTSTEP_LEN
    out = np.zeros(n)

    # A - heel click: broadband excitation
    click_len = 4000
    click = noise(click_len, rng) * exp_decay(click_len, rng.uniform(75, 130))
    click = biquad(click, "bandpass", rng.uniform(2400, 3600), 1.2)
    place(out, click * rng.uniform(0.30, 0.55), 0)

    # B - mass thump: the weight landing, with a short downward pitch glide
    thump_onset = int(rng.uniform(0.002, 0.006) * SR)
    thump_len = 8000
    t = t_axis(thump_len)
    f_body = rng.uniform(105, 135)
    curve = f_body * (1.0 + (rng.uniform(1.25, 1.45) - 1.0) * np.exp(-t / rng.uniform(0.018, 0.030)))
    thump = np.sin(glide_phase(curve))
    thump *= raised_cos_attack(thump_len, 0.003) * exp_decay(thump_len, rng.uniform(38, 55))
    place(out, thump * rng.uniform(0.55, 0.75), thump_onset)

    # C - grit scatter: small debris, the layer that actually says "stone"
    grit = np.zeros(n)
    for _ in range(rng.integers(5, 12)):
        length = int(rng.uniform(0.003, 0.007) * SR)
        grain = noise(length, rng) * exp_decay(length, 380.0)
        place(grit, grain * rng.uniform(0.05, 0.18), int(rng.uniform(0.008, 0.110) * SR))
    out += biquad(grit, "highpass", GRIT_HIGHPASS, 0.707)

    # D - slab resonance: the stone's own ring
    mode_len = 6000
    for base in STONE_MODES:
        freq = base * rng.uniform(0.965, 1.035)
        mode = sine(mode_len, freq) * raised_cos_attack(mode_len, 0.002)
        mode *= np.exp(-t_axis(mode_len) / rng.uniform(0.022, 0.045))
        place(out, mode * rng.uniform(0.10, 0.22), thump_onset)

    out = reverb_mono(out, "footstep")
    out = peak_normalize(out, 0.50) * rng.uniform(0.75, 1.00)
    return fade_tail(out, 0.012), rng


def build_footsteps():
    paths = []
    for index, seed in enumerate((101, 102, 103, 104, 105), start=1):
        step, rng = build_footstep(seed)
        path = SFX_DIR / f"footstep_stone_{index:02d}.wav"
        write_wav(path, step, rng)
        paths.append((path, step))
        report.append((path.name, len(step) / SR, dbfs(np.max(np.abs(step))),
                       dbfs(rms(step)), band_rms_db(step, 2000, 5000)))
    return paths


# =============================================================================
# 2. Grab - reach, seize, ring
# =============================================================================
# The latch reads as "clicked into place" because the pitch glide arrives on
# exactly the frequency the bell then holds. 660 Hz is the existing Last Spark
# chime and E5 is diatonic in the music's D minor, so the grab and the attune
# chime are the same instrument.

GRAB_LEN = 88200
LATCH = 0.185
ANCHOR = 660.0


def build_grab():
    rng = np.random.default_rng(202)
    n = GRAB_LEN
    latch_at = int(LATCH * SR)
    out = np.zeros(n)

    # A - reach: filtered noise swelling upward, no pitch, pure tension
    t = t_axis(latch_at)
    sweep = 320.0 * (2400.0 / 320.0) ** (t / LATCH)
    reach = biquad_swept(noise(latch_at, rng), "bandpass", sweep, 2.2)
    place(out, reach * (t / LATCH) ** 2.2 * 0.20, 0)

    # B - the latch itself: three detuned sines gliding a minor third onto ANCHOR
    curve = np.concatenate([
        555.0 * (ANCHOR / 555.0) ** ((t / LATCH) ** 0.62),
        np.full(n - latch_at, ANCHOR),
    ])
    tail = np.concatenate([np.ones(latch_at), exp_decay(n - latch_at, 2.4)])
    env = raised_cos_attack(n, 0.040) * tail
    for detune, amp in ((0.99711, 0.10), (1.00000, 0.14), (1.00343, 0.10)):
        out += np.sin(glide_phase(curve * detune)) * env * amp

    rest = n - latch_at
    t2 = t_axis(rest)

    # C - seize tick
    tick_len = 1200
    tick = noise(tick_len, rng) * exp_decay(tick_len, 260.0)
    place(out, biquad(tick, "bandpass", 1800.0, 1.4) * 0.35, latch_at)
    for freq, amp in ((1320.0, 0.22), (2750.0, 0.14), (4481.0, 0.08), (6204.0, 0.05)):
        place(out, sine(rest, freq) * exp_decay(rest, 32.0) * amp, latch_at)

    # D - FM attune bell. Ratio 3.5 is non-integer, so the sidebands are
    # inharmonic: a bell, not brass. The index collapses bright -> pure.
    index = 0.4 + 5.6 * np.exp(-9.0 * t2)
    bell = np.sin(2 * np.pi * ANCHOR * t2 + index * np.sin(2 * np.pi * 2310.0 * t2))
    place(out, bell * raised_cos_attack(rest, 0.006) * exp_decay(rest, 3.2) * 0.42, latch_at)

    # E - sub confirm, so a hauled stone has mass
    place(out, sine(rest, 82.41) * raised_cos_attack(rest, 0.022) * exp_decay(rest, 5.5) * 0.14, latch_at)

    # F - shimmer tail. The second copy at +7.4 cents beats at 2.84 Hz; that
    # beat IS the shimmer, so no tremolo LFO is needed.
    ratios = (1.00, 2.00, 3.01, 4.17, 5.43)
    amps = (0.16, 0.09, 0.055, 0.032, 0.018)
    for copy, gain in ((1.0, 1.0), (1.00430, 0.5)):
        for k, (ratio, amp) in enumerate(zip(ratios, amps), start=1):
            decay = 3.0 * k ** 0.7
            place(out, sine(rest, ANCHOR * ratio * copy) * exp_decay(rest, decay) * amp * gain, latch_at)

    out = peak_normalize(out, 0.85)
    out = peak_normalize(reverb_mono(out, "grab"), 0.60)
    out = fade_tail(out, 0.020)

    path = SFX_DIR / "grab_attune.wav"
    write_wav(path, out, rng)
    report.append((path.name, len(out) / SR, dbfs(np.max(np.abs(out))),
                   dbfs(rms(out)), band_rms_db(out, 2000, 5000)))
    return path


# =============================================================================
# 3. Music - two loops, 3,386,880 samples each
# =============================================================================

Dm = ("Dm", (146.83, 220.00, 293.66, 349.23))
Bb = ("Bb", (116.54, 174.61, 293.66, 349.23))
F_ = ("F",  (87.31, 130.81, 261.63, 349.23))
Gm = ("Gm", (98.00, 146.83, 233.08, 392.00))
C_ = ("C",  (130.81, 196.00, 329.63, 392.00))
Am = ("Am", (110.00, 164.81, 261.63, 440.00))

LAST_SPARK = dict(
    name="music_last_spark",
    # Ends on minor v, not V7: no leading tone, so the loop point has nothing
    # left to resolve and never announces itself.
    chords=(Dm, Bb, F_, Gm, Dm, Bb, C_, Am),
    lowpass=1600.0, air_centroid=430.0, air_amp=0.11, reverb_wet=0.38,
    shimmer=True,
    bells=((19.2, 880.00, 0.55, +0.35), (24.0, 659.26, 0.40, -0.30),
           (33.6, 587.33, 0.50, +0.20), (38.4, 698.46, 0.35, -0.40),
           (48.0, 880.00, 0.45, +0.30), (52.8, 1046.50, 0.30, -0.20),
           (57.6, 659.26, 0.42, +0.40), (67.2, 587.33, 0.48, -0.25),
           (72.0, 880.00, 0.30, 0.00)),
)

SOUL_BRIDGE = dict(
    name="music_soul_bridge",
    chords=(Dm, Bb, Gm, Am),
    lowpass=1200.0, air_centroid=300.0, air_amp=0.13, reverb_wet=0.45,
    shimmer=False,
    bells=((14.4, 440.00, 0.50, +0.30), (28.8, 587.33, 0.42, -0.35),
           (43.2, 440.00, 0.38, +0.25), (57.6, 349.23, 0.45, -0.20),
           (72.0, 587.33, 0.40, 0.00)),
)

BELL_RATIOS = (1.000, 2.003, 3.011, 4.166, 5.433, 6.796)
BELL_AMPS = (1.0, 0.52, 0.31, 0.17, 0.10, 0.06)


def drone_layer():
    """Open fifth held under every chord. The longest-lived element never
    changes, which is the single biggest reason the seam is inaudible."""
    partial_amps = (1.0, 0.50, 0.28, 0.15, 0.09, 0.05)
    detunes = (0.99870, 1.00000, 1.00135)
    out = np.zeros(LOOP)
    for root, gain in ((73.42, 0.28), (110.00, 0.18)):
        for harmonic, amp in enumerate(partial_amps, start=1):
            for detune in detunes:
                out += sine(LOOP, q(root * harmonic * detune)) * amp * gain / len(detunes)
    return out


def pad_layer(chords, buffer_len):
    """4-voice pad. Each note starts and ends at exactly zero amplitude, with
    a 2.6 s release overlapping into the next chord."""
    slot = LOOP // len(chords)
    note_len = slot + int(2.6 * SR)
    detunes = (0.99820, 1.00000, 1.00190)
    positions = (0.0, -0.30, +0.30, -0.15)
    env = raised_cos_attack(note_len, 2.6) * raised_cos_release(note_len, 3.2)
    out = np.zeros((2, buffer_len))

    for index, (_, voices) in enumerate(chords):
        start = index * slot
        for freq, position in zip(voices, positions):
            voice = np.zeros(note_len)
            for detune in detunes:
                voice += sine(note_len, freq * detune) / len(detunes)
            voice += sine(note_len, freq * 2.0) * 0.22
            place(out, pan(voice * env, position) * 0.30, start)
    return out


def bell_layer(bells, buffer_len):
    """Same DNA as the two chimes already in the game, made richer. The final
    bell's tail wraps into the head of the loop, so it never starts empty."""
    out = np.zeros((2, buffer_len))
    length = int(7.0 * SR)
    attack = raised_cos_attack(length, 0.008)
    for at, freq, amp, position in bells:
        tone = np.zeros(length)
        for k, (ratio, partial_amp) in enumerate(zip(BELL_RATIOS, BELL_AMPS), start=1):
            tone += sine(length, freq * ratio) * exp_decay(length, 1.15 * k ** 0.55) * partial_amp
        place(out, pan(tone * attack, position) * amp * 0.16, int(at * SR))
    return out


def air_layer(rng, centroid, amp):
    """Built in the frequency domain: exactly periodic by construction, so it
    loops perfectly with no filter and no crossfade."""
    bed = shaped_noise(LOOP, rng, lambda f: (f / centroid) ** 1.2 / (1.0 + (f / centroid) ** 4.6))
    t = t_axis(LOOP)
    breath = 0.55 + 0.45 * np.sin(2 * np.pi * t / 25.6)      # L/3
    position = 0.35 * np.sin(2 * np.pi * t / 38.4)           # L/2
    angle = (position + 1.0) * np.pi / 4.0
    voice = bed * breath * amp
    return np.stack([voice * np.cos(angle), voice * np.sin(angle)])


def shimmer_layer():
    t = t_axis(LOOP)
    env = 0.030 * (0.35 + 0.65 * (0.5 - 0.5 * np.cos(2 * np.pi * t / 38.4)))
    out = np.zeros(LOOP)
    for freq in (1174.66, 1760.00):
        for detune in (1.0, 1.0025):
            out += sine(LOOP, q(freq * detune)) * env / 2.0
    return np.stack([out, out])


def build_music(preset):
    rng = np.random.default_rng(303)
    padded = LOOP + TAIL

    # Decaying layers get headroom, then the overhang is folded back in.
    pad = pad_layer(preset["chords"], padded)
    bells = bell_layer(preset["bells"], padded)
    for layer in (pad, bells):
        layer[:, :TAIL] += layer[:, LOOP:LOOP + TAIL]
    pad, bells = pad[:, :LOOP], bells[:, :LOOP]

    drone = drone_layer()
    # The lowpass is the masking budget: it empties the 2-5 kHz octave where the
    # footstep click and grit live. It applies to the drone and pad bus ONLY -
    # the bells are the one voice allowed up there, and only nine times a loop.
    tonal = butterworth_lowpass(pad + np.stack([drone, drone]), preset["lowpass"], order=6)

    mix = tonal + bells + air_layer(rng, preset["air_centroid"], preset["air_amp"])
    if preset["shimmer"]:
        mix = mix + shimmer_layer()

    # Circular reverb on the unpadded mix: the tail wraps into the head, which
    # is exactly what makes the seam disappear. No fade in or out.
    mix = reverb_stereo(mix, "music", wet=preset["reverb_wet"])
    mix = peak_normalize(mix, 0.63)

    path = MUSIC_DIR / f"{preset['name']}.wav"
    write_wav(path, mix, rng)
    report.append((path.name, mix.shape[1] / SR, dbfs(np.max(np.abs(mix))),
                   dbfs(rms(mix)), band_rms_db(mix, 2000, 5000)))
    return path, mix


# =============================================================================
# 4. MIDI sources for FL Studio
# =============================================================================

def build_midi(preset):
    slot_beats = (LOOP // len(preset["chords"])) / BEAT
    chords = preset["chords"]

    drone = [(0.0, LOOP / BEAT, midi.hz_to_midi(f), 70) for f in (73.42, 110.00)]
    pad = []
    for index, (_, voices) in enumerate(chords):
        start = index * slot_beats
        for voice_index, freq in enumerate(voices):
            pad.append((start, slot_beats + 0.5, midi.hz_to_midi(freq), 62 - voice_index * 4))
    bells = [(at * BPM / 60.0, 6.0, midi.hz_to_midi(freq), int(40 + amp * 80))
             for at, freq, amp, _ in preset["bells"]]

    path = MUSIC_DIR / f"{preset['name']}.mid"
    midi.write_midi(path, [
        {"name": "Drone", "notes": drone, "channel": 0, "program": 89},
        {"name": "Pad", "notes": pad, "channel": 1, "program": 89},
        {"name": "Bells", "notes": bells, "channel": 2, "program": 14},
    ], BPM)
    return path


# =============================================================================
# main
# =============================================================================

def read_wav(path):
    import wave as _wave
    with _wave.open(str(path), "rb") as f:
        frames = f.getnframes()
        channels = f.getnchannels()
        raw = np.frombuffer(f.readframes(frames), dtype=np.int16).astype(np.float64) / 32768.0
    return raw if channels == 1 else raw.reshape(-1, channels).T


def check_only():
    """Re-run every gate against the files already on disk, without regenerating.
    Use after re-exporting a loop from FL Studio (see docs/audio/FL-STUDIO-RECIPE.md)."""
    music = {p.stem: read_wav(p) for p in sorted(MUSIC_DIR.glob("*.wav"))}
    steps = [(p, read_wav(p)) for p in sorted(SFX_DIR.glob("footstep_stone_*.wav"))]
    if not music or not steps:
        print("nothing to check; run without --check-only first")
        return 1
    return verify(music, steps)


def main():
    for directory in (SFX_DIR, MUSIC_DIR, PREVIEW_DIR):
        directory.mkdir(parents=True, exist_ok=True)

    print("footsteps...")
    steps = build_footsteps()
    print("grab...")
    build_grab()

    music = {}
    for preset in (LAST_SPARK, SOUL_BRIDGE):
        print(f"{preset['name']}... (this takes a moment)")
        _, mix = build_music(preset)
        music[preset["name"]] = mix
        build_midi(preset)

    # ---- previews -------------------------------------------------------
    rng = np.random.default_rng(404)
    cadence = int(0.40 * SR)
    walk = np.zeros(cadence * 12 + FOOTSTEP_LEN)
    for i in range(12):
        _, step = steps[rng.integers(0, len(steps))]
        place(walk, step * rng.uniform(0.85, 1.0), i * cadence)
    write_wav(PREVIEW_DIR / "preview_footsteps.wav", peak_normalize(walk, 0.8), rng)

    for name, mix in music.items():
        seam = np.concatenate([mix, mix], axis=1)
        write_wav(PREVIEW_DIR / f"preview_{name}_x2.wav", seam, rng)

    # ---- report ---------------------------------------------------------
    print()
    print(f"{'asset':<32} {'secs':>7} {'peak dB':>9} {'rms dB':>8} {'2-5k dB':>9}")
    print("-" * 70)
    for name, seconds, peak, level, band in report:
        print(f"{name:<32} {seconds:>7.2f} {peak:>9.1f} {level:>8.1f} {band:>9.1f}")
    print()

    return verify(music, steps)


def verify(music, steps):
    failures = []

    for name, mix in music.items():
        if mix.shape[1] != LOOP:
            failures.append(f"{name}: length {mix.shape[1]} != {LOOP}")
        # A seamless loop does not have a near-zero jump at the wrap - x[0] and
        # x[-1] are adjacent samples, so they differ by one ordinary sample step.
        # The real test is that the wrap looks like any other sample boundary.
        # A click is a DISCONTINUITY. Measure the actual second difference across
        # the join on the periodic extension and check it is in-family with the
        # file's own curvature. Testing the step size instead gives a false
        # positive: every oscillator starts at phase 0, so the wrap deliberately
        # sits where all the partials' derivatives align and the waveform is steep.
        mono = np.mean(mix, axis=0)
        join = max(abs(mono[1] - 2 * mono[0] + mono[-1]),
                   abs(mono[0] - 2 * mono[-1] + mono[-2]))
        reference = float(np.percentile(np.abs(np.diff(mono, n=2)), 99.9))
        print(f"{name}: join curvature {join:.2e} vs 99.9th pctile {reference:.2e} "
              f"(x{join / reference:.2f})")
        if join > reference:
            failures.append(f"{name}: curvature at the loop join is "
                            f"{join / reference:.1f}x the file's 99.9th percentile - audible click")
        band = band_rms_db(mix, 2000, 5000)
        if band > -48.0:
            failures.append(f"{name}: 2-5 kHz RMS {band:.1f} dB exceeds the -48 dB masking budget")

    # Measure the footstep transient, not the 0.5 s file: the click lasts ~30 ms
    # and averaging it across the silent reverb tail buries it by ~25 dB.
    quietest_music = max(band_rms_db(mix, 2000, 5000) for mix in music.values())
    for path, step in steps:
        band = band_rms_db(step[:int(0.05 * SR)], 2000, 5000)
        headroom = band - quietest_music
        print(f"{path.name}: 2-5 kHz transient {band:.1f} dB, {headroom:.1f} dB above the music")
        if headroom < 15.0:
            failures.append(f"{path.name}: only {headroom:.1f} dB above the music in 2-5 kHz; "
                            f"footsteps will be masked")

    print()
    if failures:
        for line in failures:
            print("FAIL:", line)
        return 1
    print("OK: loop seams clean, masking budget met.")
    return 0


if __name__ == "__main__":
    raise SystemExit(check_only() if "--check-only" in sys.argv else main())
