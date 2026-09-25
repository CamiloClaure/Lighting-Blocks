"""Generate four original dry crystal pickups using only Python's standard library.
Run from anywhere: python Tools/audio/build_pickup_audio.py
44.1 kHz mono PCM16, .36 peak, smooth attack and zero-ended tail; deterministic.
"""
from pathlib import Path
import math, random, struct, wave
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/LightingBlocks/Resources/Audio/LightingPickups'
OUT.mkdir(parents=True,exist_ok=True)
for variant,duration in enumerate((.21,.225,.24,.255),1):
    rate=44100; rng=random.Random(1973+variant); data=[]
    for n in range(int(duration*rate)):
        t=n/rate
        attack=1-math.exp(-t/.003)
        tail=min(1,(duration-t)/.025)**2
        f=660*(1+(variant-2.5)*.003)
        bell=(math.sin(2*math.pi*f*t)*math.exp(-t/.067)
              +.18*math.sin(2*math.pi*f*2*t)*math.exp(-t/.038))
        pop=.35*math.sin(2*math.pi*(210*t+65*.012*(1-math.exp(-t/.012))))*math.exp(-t/.017)
        u=max(0,t-.028)
        sparkle=.17*(1-math.exp(-u/.007))*math.exp(-u/.043)*math.sin(2*math.pi*(990*u+900*u*u))
        noise=.025*(rng.random()*2-1)*math.exp(-t/.009)
        data.append((bell+pop+sparkle+noise)*attack*tail)
    scale=.36/max(abs(v) for v in data)
    with wave.open(str(OUT/f'pickup_{variant:02}.wav'),'wb') as wav:
        wav.setparams((1,2,rate,0,'NONE','not compressed'))
        wav.writeframes(b''.join(struct.pack('<h',round(v*scale*32767)) for v in data))
    print(f'pickup_{variant:02}: {duration:.3f}s, peak .36')
