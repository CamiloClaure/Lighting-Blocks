# Rebuilding the Lighting Blocks music in FL Studio

The shipped `.wav` loops are rendered by `Tools/audio/build_audio.py`. This document
is for when you want to *edit* the music rather than regenerate it — it reproduces the
same arrangement natively in FL Studio using stock plugins.

There is deliberately no `.flp` in this repo. The format is reverse-engineered, its
instrument state lives in undocumented binary blobs, and nothing here can open FL Studio
to verify a generated file. The `.mid` files are the source of truth instead: they are an
open format FL imports natively and losslessly.

---

## 1. Import the MIDI

`File ▸ Import ▸ MIDI file` → pick `Assets/Resources/Audio/Music/music_soul_bridge.mid`
→ choose **import all tracks**.

You get three channels, already named:

| Track | Notes | What it is |
|---|---|---|
| **Drone** | 2 | D2 + A2 open fifth, held for the whole loop |
| **Pad** | 8 | The 4-voice chord progression |
| **Bells** | 5 | Sparse chimes |

Set the project tempo to **50 BPM** if it did not come across. This matters: at 50 BPM one
bar is exactly 4.8 s and the 16-bar loop is exactly 76.800 s, which is what makes the loop
sample-accurate.

---

## 2. Instruments

### Drone — 3xOSC
Three oscillators, all **sine**, all on the same note:

| OSC | Coarse | Fine detune | Volume |
|---|---|---|---|
| 1 | 0 | **−2.3 cents** | 100% |
| 2 | 0 | 0 | 100% |
| 3 | 0 | **+2.3 cents** | 100% |

Envelope: attack 2 s, sustain full, release 4 s. The detune is the entire point — it
produces a slow beating that keeps a static drone alive. Do not widen it past ±4 cents or
it starts to sound out of tune rather than shimmering.

The rendered version stacks 6 harmonic partials at amplitudes 1.0 / 0.50 / 0.28 / 0.15 /
0.09 / 0.05. In 3xOSC just use a sine and let the reverb and the lowpass do the shaping,
or use Sytrus with those six partials if you want the exact timbre.

### Pad — Sytrus, or 3xOSC per voice
Same ±3.2 cents detune spread across three sines, plus a second harmonic at 22% volume.

Envelope is the character of this layer, so match it:
- **Attack 2.6 s** (raised cosine / smooth, not linear)
- **Release 3.2 s**

The release deliberately overlaps 2.6 s into the next chord. That overlap is what makes the
progression breathe instead of stepping. In the piano roll the notes are already written
long enough to overlap — do not shorten them to fit the bars.

### Bells — Sytrus FM, or FL Keys
Inharmonic partial ratios, which is what makes it a bell rather than an organ:

| Partial | Ratio | Level | Decay |
|---|---|---|---|
| 1 | 1.000 | 100% | 6.0 s |
| 2 | 2.003 | 52% | 3.9 s |
| 3 | 3.011 | 31% | 3.1 s |
| 4 | 4.166 | 17% | 2.6 s |
| 5 | 5.433 | 10% | 2.3 s |
| 6 | 6.796 | 6% | 2.2 s |

Attack 8 ms, no sustain, decay only. Higher partials must die first — that is the physics
of a struck bell and it is what stops it sounding like a synth pad.

### Air bed — Fruity Noise Generator or a white-noise sample
Heavily filtered noise: a broad rise up to about **300 Hz**, then
a steep rolloff above it. Add a slow volume LFO with a period of **25.6 s** and a pan LFO
at **38.4 s**. Keep it at roughly −19 dB — it should be felt, not heard.

---

## 3. Mixer

Route Drone and Pad to one bus, Bells and Air to another.

**On the Drone + Pad bus only:**
- **Fruity Filter / EQ: lowpass at 1200 Hz**, steep.

This one filter is the reason footsteps stay audible over the music. It empties the
2–5 kHz octave, which is exactly where the footstep click and grit sit. The bells are the
only voice allowed up there, and only nine times in 76.8 seconds. If you raise this cutoff,
footsteps start to disappear under the music.

**On the master, Fruity Reeverb 2:**

| Parameter | Value |
|---|---|
| Pre-delay | 50 ms |
| Decay (RT60) | 4.4 s |
| High damping | heavy — the tail should darken as it decays |
| Stereo separation | wide |
| Wet | 45% |

Large, stony, absorptive. The tail is long on purpose: it is what wraps the end of the loop
back into its beginning.

**Levels:** master peak around **−4 dBFS**, integrated RMS around **−18 dBFS**. In Unity
the music plays at volume 0.18, so it lands near −33 dBFS in game.

---

## 4. Exporting a replacement loop

`File ▸ Export ▸ Wave file`, then:

- **Mode: leave tails inside the loop.** Do not let the reverb tail run past the end and do
  not add a fade — either one destroys the seamless loop.
- Length **exactly 76.800 s** (16 bars at 50 BPM).
- **44100 Hz, 16-bit**, stereo.
- Save over `Assets/Resources/Audio/Music/music_soul_bridge.wav`.

Then verify the loop still joins cleanly:

```
python3 Tools/audio/build_audio.py --check-only
```

This re-runs every gate against the files on disk without regenerating them: loop-join
curvature, and the 2-5 kHz separation between the music and the footsteps. Or just
concatenate the file to itself in any editor and listen across the join twice. A
seam is either obvious or absent; there is no middle ground.

---

## 5. The musical decisions, and why

- **D natural minor.** The two chimes already synthesized in C# are 660 Hz (E5) and 880 Hz
  (A5). Both are diatonic in D minor, so every existing sound effect is consonant with the
  music by construction and nothing already shipped needed retuning.
- **50 BPM.** Chosen because 2646000 / 50 is a whole number, so every beat, bar and chord
  boundary lands on an exact sample. This is what makes the loop sample-accurate.
- **The progression ends on minor v (Am), not V7.** No leading tone means the loop point has
  nothing left to resolve, so the ear does not hear the loop restart.
- **The drone never changes chord.** It sits under Dm, Bb, F, Gm, C and Am alike, becoming
  Bb/D, Dm7, Cadd9/D and Dm11 in turn — all consonant, none cadential. The longest-lived
  element in the mix being completely static is the single biggest reason the seam is
  inaudible.
