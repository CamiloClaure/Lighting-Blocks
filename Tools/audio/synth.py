"""Synthesis primitives for the Lighting Blocks audio assets.

Pure numpy + stdlib. No scipy, no ffmpeg, no audio libraries.

Everything is float64 in [-1, 1] internally and written as 44100 Hz / 16-bit PCM,
matching the two chimes already synthesized in C# (LastSparkGame.cs:52-56 and
SoulBridgeGame.cs:52-56).
"""

import wave

import numpy as np

SR = 44100


# --------------------------------------------------------------------------
# output
# --------------------------------------------------------------------------

def write_wav(path, data, rng=None):
    """Write float data to 16-bit PCM. `data` is (n,) mono or (2, n) stereo."""
    data = np.asarray(data, dtype=np.float64)
    if data.ndim == 1:
        interleaved, channels = data, 1
    else:
        channels = data.shape[0]
        interleaved = data.T.reshape(-1)

    # TPDF dither at 1 LSB. The music sits near -21 dBFS and is attenuated again
    # by AudioSource.volume, so quantization noise on a quiet drone is audible.
    if rng is not None:
        n = interleaved.size
        interleaved = interleaved + (rng.random(n) - rng.random(n)) / 32768.0

    clipped = np.clip(interleaved, -1.0, 1.0)
    pcm = (clipped * 32767.0).astype(np.int16)

    with wave.open(str(path), "wb") as f:
        f.setnchannels(channels)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(pcm.tobytes())
    return path


# --------------------------------------------------------------------------
# envelopes and oscillators
# --------------------------------------------------------------------------

def t_axis(n):
    return np.arange(n, dtype=np.float64) / SR


def raised_cos_attack(n, seconds):
    """Envelope that rises 0 -> 1 over `seconds` then holds at 1."""
    env = np.ones(n)
    a = min(int(seconds * SR), n)
    if a > 0:
        env[:a] = 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, a))
    return env


def raised_cos_release(n, seconds):
    """Envelope that holds at 1 then falls to exactly 0 over the last `seconds`."""
    env = np.ones(n)
    r = min(int(seconds * SR), n)
    if r > 0:
        env[n - r:] = 0.5 + 0.5 * np.cos(np.linspace(0.0, np.pi, r))
    return env


def exp_decay(n, k):
    """exp(-k*t), the decay shape both existing in-game chimes use."""
    return np.exp(-k * t_axis(n))


def sine(n, freq, phase=0.0):
    return np.sin(2.0 * np.pi * freq * t_axis(n) + phase)


def noise(n, rng):
    return rng.standard_normal(n) * 0.33


def fade_tail(x, seconds):
    """Raised-cosine fade on the last `seconds`. Never use this on a loop."""
    r = int(seconds * SR)
    if r > 0 and r < x.shape[-1]:
        ramp = 0.5 + 0.5 * np.cos(np.linspace(0.0, np.pi, r))
        x[..., -r:] *= ramp
    return x


def peak_normalize(x, target):
    peak = np.max(np.abs(x))
    return x * (target / peak) if peak > 0 else x


# --------------------------------------------------------------------------
# RBJ biquads (SFX only - the music filters in the frequency domain)
# --------------------------------------------------------------------------

def biquad_coeffs(kind, f0, q):
    w0 = 2.0 * np.pi * f0 / SR
    cos_w0, sin_w0 = np.cos(w0), np.sin(w0)
    alpha = sin_w0 / (2.0 * q)
    a0, a1, a2 = 1.0 + alpha, -2.0 * cos_w0, 1.0 - alpha
    if kind == "bandpass":
        b0, b1, b2 = alpha, 0.0, -alpha
    elif kind == "lowpass":
        b0 = (1.0 - cos_w0) / 2.0
        b1, b2 = 1.0 - cos_w0, b0
    elif kind == "highpass":
        b0 = (1.0 + cos_w0) / 2.0
        b1, b2 = -(1.0 + cos_w0), b0
    else:
        raise ValueError(kind)
    return b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0


def biquad(x, kind, f0, q):
    """Direct Form I. Buffers here are <= 88200 samples; a Python loop is fine."""
    b0, b1, b2, a1, a2 = biquad_coeffs(kind, f0, q)
    y = np.zeros_like(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, xn in enumerate(x):
        yn = b0 * xn + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        y[i] = yn
        x2, x1 = x1, xn
        y2, y1 = y1, yn
    return y


def biquad_swept(x, kind, f0_curve, q, block=64):
    """Bandpass with a moving centre frequency, recomputed every `block` samples."""
    y = np.zeros_like(x)
    x1 = x2 = y1 = y2 = 0.0
    for start in range(0, len(x), block):
        stop = min(start + block, len(x))
        b0, b1, b2, a1, a2 = biquad_coeffs(kind, f0_curve[start], q)
        for i in range(start, stop):
            xn = x[i]
            yn = b0 * xn + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
            y[i] = yn
            x2, x1 = x1, xn
            y2, y1 = y1, yn
    return y


# --------------------------------------------------------------------------
# reverb
# --------------------------------------------------------------------------
# Schroeder/Freeverb written as a frequency-domain transfer function. Every
# element is LTI, so H(f) has a closed form on the rfft bin grid: evaluate,
# multiply, inverse. Exact, ~15 lines, and *circular* - which is precisely the
# tail-wrapping behaviour the music loops want for free.

REVERB = {
    #                combs                                        allpasses            g      a     b    predelay wet
    "footstep": dict(combs=(613, 653, 701, 747, 783, 821, 857, 889),
                     allpass=(307, 241, 187, 127), g=0.80, a=0.72, b=0.28, predelay=397, wet=0.11),
    "grab": dict(combs=(1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617),
                 allpass=(556, 441, 341, 225), g=0.88, a=0.62, b=0.38, predelay=1323, wet=0.24),
    "music": dict(combs=(1317, 1403, 1507, 1601, 1679, 1759, 1837, 1909),
                  allpass=(657, 521, 403, 265), g=0.945, a=0.54, b=0.46, predelay=2205, wet=0.38),
}


def reverb_transfer(n, preset, spread=0):
    """H(f) on the rfft bin grid. `spread` offsets every delay (stereo width)."""
    p = REVERB[preset] if isinstance(preset, str) else preset
    k = np.arange(n // 2 + 1)
    step = -2j * np.pi * k / n

    def z(d):
        return np.exp(step * d)

    z1 = z(1)
    damper = p["a"] + p["b"] * z1  # 2-tap FIR damper; has a closed form on the grid

    h = np.zeros(n // 2 + 1, dtype=np.complex128)
    for d in p["combs"]:
        h += 1.0 / (1.0 - p["g"] * damper * z(d + spread))
    h /= len(p["combs"])

    for d in p["allpass"]:
        zd = z(d + spread)
        h *= (-0.5 + zd) / (1.0 - 0.5 * zd)

    return h * z(p["predelay"])


def _wet(x, preset, spread):
    h = reverb_transfer(len(x), preset, spread)
    out = np.fft.irfft(np.fft.rfft(x) * h, n=len(x))
    dry_rms, wet_rms = rms(x), rms(out)
    return out * (dry_rms / wet_rms) if wet_rms > 0 else out


def reverb_mono(x, preset):
    """For SFX. Zero-pad BEFORE calling so the tail decays inside the buffer."""
    w = REVERB[preset]["wet"]
    return (1.0 - w) * x + w * _wet(x, preset, 0)


def reverb_stereo(x, preset, wet=None):
    """For music. Do NOT pad - the circular wrap is what hides the loop seam.

    `x` is (2, n). Right channel uses left delays + 23 (Freeverb stereo spread).
    """
    w = REVERB[preset]["wet"] if wet is None else wet
    left = (1.0 - w) * x[0] + w * _wet(x[0], preset, 0)
    right = (1.0 - w) * x[1] + w * _wet(x[1], preset, 23)
    return np.stack([left, right])


# --------------------------------------------------------------------------
# frequency-domain helpers (music only - every music layer is loop-periodic,
# so circular FFT filtering is the correct operation, not an approximation)
# --------------------------------------------------------------------------

def butterworth_lowpass(x, cutoff, order=6):
    n = x.shape[-1]
    f = np.fft.rfftfreq(n, 1.0 / SR)
    mag = 1.0 / np.sqrt(1.0 + (f / cutoff) ** (2 * order))
    return np.fft.irfft(np.fft.rfft(x) * mag, n=n)


def shaped_noise(n, rng, spectrum):
    """Noise built in the frequency domain: exactly periodic with period n by
    construction, so it loops perfectly with no filtering and no crossfade."""
    f = np.fft.rfftfreq(n, 1.0 / SR)
    mag = spectrum(np.maximum(f, 1e-6))
    phase = rng.random(f.size) * 2.0 * np.pi
    phase[0] = 0.0
    spec = mag * np.exp(1j * phase)
    out = np.fft.irfft(spec, n=n)
    return out / (np.max(np.abs(out)) or 1.0)


# --------------------------------------------------------------------------
# measurement
# --------------------------------------------------------------------------

def rms(x):
    return float(np.sqrt(np.mean(np.square(x))))


def dbfs(value):
    return -np.inf if value <= 0 else 20.0 * np.log10(value)


def band_rms_db(x, low, high):
    """RMS restricted to a frequency band, via Parseval on the rfft."""
    mono = x if x.ndim == 1 else np.mean(x, axis=0)
    n = mono.shape[-1]
    spec = np.fft.rfft(mono)
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    mask = (freqs >= low) & (freqs < high)
    # Parseval for a real FFT: bins other than DC/Nyquist count twice.
    weight = np.full(freqs.size, 2.0)
    weight[0] = 1.0
    if n % 2 == 0:
        weight[-1] = 1.0
    power = np.sum(weight[mask] * np.abs(spec[mask]) ** 2) / (n * n)
    return dbfs(np.sqrt(power))


def loop_seam(x):
    """(value discontinuity, slope discontinuity) across the wrap point."""
    mono = x if x.ndim == 1 else np.mean(x, axis=0)
    value = abs(float(mono[0] - mono[-1]))
    slope = abs(float((mono[1] - mono[0]) - (mono[-1] - mono[-2])))
    return value, slope
