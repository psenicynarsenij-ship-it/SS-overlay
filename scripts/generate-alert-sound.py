"""Generate the project's original 450 ms mono PCM alert; Python stdlib only."""
import math
import struct
import wave
from pathlib import Path
path = Path(__file__).resolve().parents[1] / 'assets/sounds/hp-alert.wav'
path.parent.mkdir(parents=True, exist_ok=True)
rate, duration = 44100, 0.45
samples = []
for i in range(int(rate * duration)):
    t = i / rate
    frequency = 880 if t < 0.22 else 1100
    envelope = min(1, t / 0.015, (duration - t) / 0.04)
    # Fade around the transition as well, avoiding a discontinuity/click.
    envelope *= min(1, abs(t - 0.22) / 0.008)
    samples.append(struct.pack('<h', round(0.35 * 32767 * envelope * math.sin(2 * math.pi * frequency * t))))
with wave.open(str(path), 'wb') as output:
    output.setnchannels(1)
    output.setsampwidth(2)
    output.setframerate(rate)
    output.writeframes(b''.join(samples))
