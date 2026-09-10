"""Minimal Standard MIDI File (type 1) writer.

No dependency. FL Studio imports these natively via File > Import > MIDI file,
creating one Channel Rack channel per named track.
"""

import struct

PPQ = 480


def _vlq(value):
    """MIDI variable-length quantity."""
    out = bytearray([value & 0x7F])
    value >>= 7
    while value:
        out.insert(0, (value & 0x7F) | 0x80)
        value >>= 7
    return bytes(out)


def _chunk(tag, payload):
    return tag + struct.pack(">I", len(payload)) + payload


def _track(name, notes, channel, tempo_bpm=None, program=None):
    """notes: iterable of (start_beats, duration_beats, midi_note, velocity)."""
    events = []  # (tick, order, bytes) - order keeps note-offs before note-ons

    if tempo_bpm is not None:
        us = int(round(60_000_000 / tempo_bpm))
        events.append((0, 0, b"\xFF\x51\x03" + struct.pack(">I", us)[1:]))

    label = name.encode("ascii", "replace")
    events.append((0, 0, b"\xFF\x03" + _vlq(len(label)) + label))

    if program is not None:
        events.append((0, 1, bytes([0xC0 | channel, program])))

    for start, duration, note, velocity in notes:
        on = int(round(start * PPQ))
        off = int(round((start + duration) * PPQ))
        note = max(0, min(127, int(note)))
        events.append((on, 2, bytes([0x90 | channel, note, max(1, min(127, int(velocity)))])))
        events.append((off, 1, bytes([0x80 | channel, note, 0])))

    events.sort(key=lambda e: (e[0], e[1]))

    payload = bytearray()
    previous = 0
    for tick, _, data in events:
        payload += _vlq(tick - previous) + data
        previous = tick
    payload += _vlq(0) + b"\xFF\x2F\x00"
    return _chunk(b"MTrk", bytes(payload))


def write_midi(path, tracks, tempo_bpm):
    """tracks: list of dicts with keys name, notes, channel and optional program."""
    header = _chunk(b"MThd", struct.pack(">HHH", 1, len(tracks), PPQ))
    body = b"".join(
        _track(t["name"], t["notes"], t["channel"],
               tempo_bpm=tempo_bpm if i == 0 else None,
               program=t.get("program"))
        for i, t in enumerate(tracks)
    )
    with open(path, "wb") as f:
        f.write(header + body)
    return path


def hz_to_midi(hz):
    """Nearest MIDI note number for a frequency (A4 = 440 Hz = note 69)."""
    import math
    return int(round(69 + 12 * math.log2(hz / 440.0)))
