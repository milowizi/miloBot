import os, wave
import numpy as np
from scipy.signal import butter, lfilter

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "sounds")
rng = np.random.default_rng(7)


def t(d):
    return np.arange(int(SR * d)) / SR


def glide(points, n):
    xs = np.linspace(0, 1, len(points))
    return np.interp(np.linspace(0, 1, n), xs, points)


def bp(x, lo, hi, order=2):
    b, a = butter(order, [lo / (SR / 2), min(hi, SR / 2 - 100) / (SR / 2)], "band")
    return lfilter(b, a, x)


def env(n, attack, shape):
    e = np.ones(n)
    a = max(1, int(SR * attack))
    e[:a] = np.sin(np.linspace(0, np.pi / 2, a))
    r = np.linspace(0, 1, n - a)
    e[a:] = (1 - r) ** shape
    return e


def squeak(d, freqs, harm=(1, 0.35, 0.12), vib=0.0, rate=7.0, attack=0.008, shape=1.6, breath=0.0):
    n = int(SR * d)
    f = glide(freqs, n)
    if vib:
        f = f * (1 + vib * np.sin(2 * np.pi * rate * t(d)))
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = sum(a * np.sin((k + 1) * ph) for k, a in enumerate(harm))
    if breath:
        x = x + breath * bp(rng.standard_normal(n), 2500, 7000)
    return x * env(n, attack, shape)


def silence(d):
    return np.zeros(int(SR * d))


def norm(x, peak):
    return x / (np.max(np.abs(x)) + 1e-9) * peak


def save(name, x):
    os.makedirs(OUT, exist_ok=True)
    fade = min(len(x) // 10, int(SR * 0.008))
    x = x.copy()
    x[-fade:] *= np.linspace(1, 0, fade)
    data = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


def make():
    save("yip", norm(squeak(0.1, [1250, 1800, 1350], (1, 0.4, 0.15), shape=1.5), 0.4))

    arf = lambda f: squeak(0.11, [f, f * 1.45, f * 1.05], (1, 0.55, 0.25, 0.1), shape=1.8)
    save("bark", norm(np.concatenate([arf(950), silence(0.07), arf(1050)]), 0.42))

    save("happy", norm(squeak(0.24, [1300, 1900, 2300], (1, 0.25, 0.08), vib=0.015, rate=9, attack=0.015, shape=1.2), 0.32))

    n = int(SR * 0.6)
    whine = squeak(0.6, [1500, 1750, 1620, 1820, 1400], (1, 0.2, 0.05), vib=0.02, rate=6, attack=0.06, shape=0.9)
    whine *= np.sin(np.linspace(0, np.pi, n)) ** 0.5
    save("whine", norm(whine, 0.26))

    n = int(SR * 0.7)
    yawn = squeak(0.7, [820, 1250, 1350, 1050, 640], (1, 0.3, 0.1), vib=0.01, rate=5, attack=0.1, shape=0.8, breath=0.02)
    yawn *= np.sin(np.linspace(0, np.pi, n)) ** 0.6
    save("yawn", norm(yawn, 0.24))

    parts = []
    for i in range(3):
        m = int(SR * 0.03)
        s = bp(rng.standard_normal(m), 3000, 7500) * np.sin(np.linspace(0, np.pi, m)) ** 2
        parts += [s, silence(0.06)]
    save("sniff", norm(np.concatenate(parts), 0.12))

    burst = bp(rng.standard_normal(int(SR * 0.04)), 2500, 8000) * env(int(SR * 0.04), 0.002, 3)
    save("sneeze", norm(np.concatenate([silence(0.02), 0.5 * burst, squeak(0.08, [1700, 1000], (1, 0.3), shape=2)]), 0.28))

    n = int(SR * 0.45)
    growl = squeak(0.45, [330, 350, 310], (1, 0.5, 0.3, 0.15), attack=0.04, shape=0.7)
    growl *= 0.6 + 0.4 * np.sin(2 * np.pi * 22 * t(0.45))
    save("growl", norm(growl, 0.22))

    thud = squeak(0.12, [150, 80], (1, 0.2), attack=0.002, shape=3)
    save("thud", norm(thud, 0.3))

    save("boop", norm(squeak(0.06, [650, 1050], (1, 0.15), attack=0.003, shape=2.5), 0.25))

    parts = []
    for i in range(4):
        parts += [squeak(0.07, [900, 1100], (1, 0.2), attack=0.01, shape=1.5, breath=0.05) * (1 if i % 2 == 0 else 0.7), silence(0.07)]
    save("pant", norm(np.concatenate(parts), 0.15))

    n = int(SR * 1.4)
    howl = squeak(1.4, [700, 1050, 1250, 1300, 1250, 1150], (1, 0.35, 0.12), vib=0.025, rate=5.5, attack=0.15, shape=0.6)
    howl *= np.sin(np.linspace(0, np.pi, n)) ** 0.4
    save("howl", norm(howl, 0.26))

    parts = []
    for i in range(3):
        m = int(SR * 0.035)
        crack = bp(rng.standard_normal(m), 1500, 5000) * env(m, 0.002, 4)
        parts += [crack + 0.4 * squeak(0.035, [300, 200], (1,), attack=0.002, shape=3), silence(0.09)]
    save("crunch", norm(np.concatenate(parts), 0.22))

    sq = squeak(0.16, [1900, 2600, 2100], (1, 0.5, 0.25), vib=0.04, rate=30, attack=0.005, shape=1.2)
    save("squeak", norm(sq, 0.28))

    chirp = np.concatenate([squeak(0.06, [3200, 4200], (1, 0.2), attack=0.004, shape=1.5), silence(0.05),
                            squeak(0.07, [3400, 4600, 3800], (1, 0.2), attack=0.004, shape=1.5)])
    save("chirp", norm(chirp, 0.2))

    m = int(SR * 0.05)
    click = bp(rng.standard_normal(m), 2000, 9000) * env(m, 0.001, 6)
    save("shutter", norm(np.concatenate([click, silence(0.06), 0.7 * click]), 0.3))

    save("pop", norm(squeak(0.05, [900, 400], (1, 0.3), attack=0.002, shape=2.5), 0.22))


if __name__ == "__main__":
    make()
    print(sorted(os.listdir(OUT)))
