# Gera o fundo retrô e os sons do tema PlayStation 1 (Assets/ps1). Rode: python tools/gerar-assets-ps1.py
# Precisa de: pip install pillow numpy
import math, random, wave
import numpy as np
from PIL import Image, ImageDraw

OUT = "Assets/ps1/"
rnd = random.Random(1995)

# ---------------- Fundo: 384x216 com dither, ampliado 4x sem suavizar (pixel gordo de PS1) ----------------
W, H = 384, 216
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0

def lerp(a, b, t): return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))

def sky():
    arr = np.zeros((H, W, 3), dtype=float)
    hor = int(H * 0.70)
    stops = [(0.0, (2, 3, 16)), (0.45, (22, 12, 82)), (1.0, (110, 60, 190))]
    for y in range(H):
        t = min(1.0, y / hor)
        for i in range(len(stops) - 1):
            if stops[i][0] <= t <= stops[i + 1][0]:
                u = (t - stops[i][0]) / (stops[i + 1][0] - stops[i][0])
                arr[y, :] = lerp(stops[i][1], stops[i + 1][1], u)
    return arr, hor

def dither(arr):
    # 5 bits por canal + dither ordenado, igual ao console
    out = np.zeros_like(arr)
    for y in range(H):
        for x in range(W):
            th = BAYER[y % 4, x % 4]
            for c in range(3):
                v = arr[y, x, c] / 8.0
                f = math.floor(v)
                out[y, x, c] = (f + (1 if (v - f) > th else 0)) * 8
    return np.clip(out, 0, 255)

arr, hor = sky()
img = Image.fromarray(dither(arr).astype("uint8"), "RGB")
d = ImageDraw.Draw(img)

# estrelas
for _ in range(190):
    x, y = rnd.randrange(W), rnd.randrange(int(hor * 0.95))
    c = rnd.choice([(255, 255, 255), (200, 190, 255), (150, 190, 255)])
    d.point((x, y), fill=c)
for _ in range(14):
    x, y = rnd.randrange(8, W - 8), rnd.randrange(6, int(hor * 0.7))
    c = (255, 255, 255)
    for dx, dy in [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (2, 0), (-2, 0), (0, 2), (0, -2)]:
        d.point((x + dx, y + dy), fill=c if abs(dx) + abs(dy) < 2 else (170, 160, 255))

# lua low-poly (esfera facetada, sombreamento chapado como nos jogos 3D de 1996)
cx, cy, R = 280, 92, 62
def ring_pts(r, n, off=0.0):
    return [(cx + r * math.cos(2 * math.pi * i / n + off), cy + r * math.sin(2 * math.pi * i / n + off)) for i in range(n)]

# anel de trás
def ring_arc(a0, a1, rx, ry, tilt, th, col):
    pts = []
    steps = 40
    for i in range(steps + 1):
        a = a0 + (a1 - a0) * i / steps
        x, y = rx * math.cos(a), ry * math.sin(a)
        xr = x * math.cos(tilt) - y * math.sin(tilt)
        yr = x * math.sin(tilt) + y * math.cos(tilt)
        pts.append((cx + xr, cy + yr))
    d.line(pts, fill=col, width=th)

ring_arc(math.pi, 2 * math.pi, 104, 24, -0.38, 4, (96, 80, 190))

rings = [(0, 1), (16, 6), (32, 12), (48, 18), (R, 24)]
verts = []
for r, n in rings:
    off = rnd.random() * 0.5
    verts.append([(cx + (r + rnd.uniform(-1.5, 1.5) * (r > 0)) * math.cos(2 * math.pi * i / n + off),
                   cy + (r + rnd.uniform(-1.5, 1.5) * (r > 0)) * math.sin(2 * math.pi * i / n + off)) for i in range(n)])
light = (-0.55, -0.65)
def shade(px, py, base):
    nx, ny = (px - cx) / R, (py - cy) / R
    lam = max(0.0, 1 - (nx * nx + ny * ny)) ** 0.5
    dot = -(nx * light[0] + ny * light[1]) * -1
    k = 0.45 + 0.55 * max(0, min(1, 0.5 + 0.5 * (nx * light[0] + ny * light[1]) * -1 + 0.25 * lam))
    k += rnd.uniform(-0.12, 0.12)
    k = max(0.15, min(1.0, k))
    return tuple(int(b * k) for b in base)

base_col = (196, 190, 245)
def tri(a, b, c):
    mx, my = (a[0] + b[0] + c[0]) / 3, (a[1] + b[1] + c[1]) / 3
    d.polygon([a, b, c], fill=shade(mx, my, base_col))
c0 = verts[0][0]
for i in range(6):
    tri(c0, verts[1][i], verts[1][(i + 1) % 6])
for k in range(1, 4):
    inner, outer = verts[k], verts[k + 1]
    ni, no = len(inner), len(outer)
    i = j = 0
    while i < ni or j < no:
        ai, ao = i / ni, j / no
        if i < ni and (j >= no or ai <= ao):
            tri(inner[i % ni], inner[(i + 1) % ni], outer[j % no]); i += 1
        else:
            tri(outer[j % no], outer[(j + 1) % no], inner[i % ni]); j += 1
# crateras (octógonos escuros)
for (px, py, r) in [(262, 78, 7), (296, 104, 9), (278, 118, 5), (300, 76, 4)]:
    d.polygon([(px + r * math.cos(a * math.pi / 4), py + r * math.sin(a * math.pi / 4)) for a in range(8)], fill=(120, 112, 190))
# anel da frente
ring_arc(0, math.pi, 104, 24, -0.38, 4, (150, 130, 250))
ring_arc(0, math.pi, 104, 24, -0.38, 1, (225, 215, 255))

# asteroides flutuando
for (ax, ay, ar) in [(206, 58, 7), (352, 40, 5), (236, 140, 6), (330, 150, 4), (60, 70, 5), (30, 120, 4)]:
    pts = [(ax + ar * rnd.uniform(0.7, 1.2) * math.cos(a), ay + ar * rnd.uniform(0.7, 1.2) * math.sin(a))
           for a in np.linspace(0, 2 * math.pi, 7, endpoint=False)]
    d.polygon(pts, fill=(54, 40, 104)); d.polygon(pts[:4], fill=(86, 66, 150))

# montanhas low-poly no horizonte
mt = [(0, hor)]
x = 0
while x < W:
    x += rnd.randint(14, 28)
    mt.append((x, hor - rnd.randint(6, 30)))
mt.append((W, hor))
d.polygon(mt, fill=(16, 8, 54))
for i in range(1, len(mt) - 1, 2):
    d.polygon([mt[i - 1], mt[i], mt[i + 1]], fill=(26, 14, 78))

# chão quadriculado em perspectiva
for y in range(hor, H):
    t = (y - hor) / (H - hor)
    base = lerp((26, 10, 70), (8, 4, 28), t)
    d.line([(0, y), (W, y)], fill=tuple(int(v) for v in base))
vx = W * 0.62
for i in range(-16, 17):
    xb = vx + i * 46
    d.line([(vx + (xb - vx) * 0.02, hor), (xb * 1.0 + (xb - vx) * 1.2, H)], fill=(140, 80, 255))
z = 0.0
y = hor
k = 1.0
while y < H:
    d.line([(0, int(y)), (W, int(y))], fill=(140, 80, 255))
    k *= 1.32
    y = hor + 1.6 * k
d.line([(0, hor), (W, hor)], fill=(230, 190, 255))

img = img.resize((W * 4, H * 4), Image.NEAREST)
img.save(OUT + "menu_bg.png", optimize=True)

# ---------------- Sons (síntese própria, 16 bits mono 44,1 kHz) ----------------
SR = 44100

def save(name, x, peak=0.8):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    with wave.open(OUT + name, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())

def reverb(x, secs=2.2, mix=0.38):
    n = int(SR * secs)
    t = np.arange(n) / SR
    ir = np.random.default_rng(7).standard_normal(n) * np.exp(-t / 0.75)
    ir[:int(0.01 * SR)] *= np.linspace(0, 1, int(0.01 * SR))
    L = len(x) + n
    wet = np.fft.irfft(np.fft.rfft(x, L) * np.fft.rfft(ir, L), L)
    wet /= (np.max(np.abs(wet)) + 1e-9)
    dry = np.pad(x, (0, L - len(x)))
    return dry * (1 - mix) + wet * np.max(np.abs(dry)) * mix

def boot():
    dur = 6.2
    t = np.arange(int(SR * dur)) / SR
    out = np.zeros_like(t)
    g = np.random.default_rng(3)
    # grave que sobe
    drone = np.sin(2 * np.pi * 73.42 * t) * np.clip(t / 2.0, 0, 1) ** 2
    drone += 0.5 * np.sin(2 * np.pi * 146.83 * t) * np.clip(t / 2.2, 0, 1) ** 2
    out += 0.9 * drone * np.exp(-np.maximum(0, t - 3.4) / 1.6)
    # acorde brilhante (Ré maior com 9ª) que vai entrando de baixo para cima
    notes = [(146.83, 0.5), (220.0, 0.9), (293.66, 1.2), (369.99, 1.5), (440.0, 1.8),
             (587.33, 2.0), (659.25, 2.2), (739.99, 2.35), (880.0, 2.5), (1174.66, 2.6), (1479.98, 2.7)]
    for f, t0 in notes:
        att = 1.25
        env = np.clip((t - t0) / att, 0, 1) ** 1.6 * np.exp(-np.maximum(0, t - (t0 + 1.7)) / 1.35)
        vib = 1 + 0.0035 * np.sin(2 * np.pi * (4.7 + g.random()) * t + g.random() * 6)
        out += 0.34 * env * (np.sin(2 * np.pi * f * vib * t) + 0.25 * np.sin(2 * np.pi * 2 * f * t))
    # varredura de "ar" subindo
    noise = g.standard_normal(len(t))
    k = np.ones(40) / 40
    band = np.convolve(noise, k, mode="same")
    env = np.clip((t - 0.3) / 1.8, 0, 1) * np.exp(-np.maximum(0, t - 2.4) / 0.5)
    out += 0.45 * band * env * np.sin(2 * np.pi * (200 + 1800 * np.clip(t / 2.6, 0, 1)) * t * 0.0 + 0)
    out += 0.30 * band * env
    out = reverb(out)
    n = len(out)
    out *= np.clip(np.arange(n) / (0.05 * SR), 0, 1) * np.clip((n - np.arange(n)) / (0.8 * SR), 0, 1)
    save("boot.wav", out, 0.85)

def blip(f0, f1, dur, wave_kind="square", vol=1.0):
    t = np.arange(int(SR * dur)) / SR
    f = f0 + (f1 - f0) * t / dur
    ph = 2 * np.pi * np.cumsum(f) / SR
    s = np.sign(np.sin(ph)) if wave_kind == "square" else np.sin(ph)
    return s * np.exp(-t / (dur * 0.35)) * vol

boot()
save("tick.wav", blip(1100, 1500, 0.06, "square") * 0.5, 0.35)
ok = np.concatenate([blip(660, 660, 0.07, "sine"), blip(990, 990, 0.16, "sine")])
save("ok.wav", ok, 0.5)
back = np.concatenate([blip(880, 880, 0.06, "sine"), blip(520, 520, 0.14, "sine")])
save("back.wav", back, 0.5)
print("ok")
