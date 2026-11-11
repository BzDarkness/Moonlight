#!/usr/bin/env python3
"""
Gera os banners (96x24) e os ícones de bloco (16x16) em pixel art da aba Jogar do Moonlight Launcher.
Uso:  python3 gerar-banners.py  <pasta-de-saida>
Precisa de: pip install pillow numpy
Os PNGs ficam em Assets/versions/ e entram no .exe como recurso.
"""
import sys, os, math, random
import numpy as np
from PIL import Image

W, H = 96, 24


def hexc(s):
    s = s.lstrip('#')
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def shade(c, f):
    return tuple(max(0, min(255, int(round(v * f)))) for v in c)


class Pix:
    def __init__(self, w=W, h=H, seed=1):
        self.w, self.h = w, h
        self.a = np.zeros((h, w, 4), dtype=np.uint8)
        self.r = random.Random(seed)

    def set(self, x, y, c, al=255):
        x, y = int(x), int(y)
        if 0 <= x < self.w and 0 <= y < self.h:
            if al >= 255:
                self.a[y, x] = (c[0], c[1], c[2], 255)
            else:
                o = self.a[y, x].astype(float)
                k = al / 255.0
                self.a[y, x] = (int(o[0] * (1 - k) + c[0] * k), int(o[1] * (1 - k) + c[1] * k),
                                int(o[2] * (1 - k) + c[2] * k), 255)

    def rect(self, x, y, w, h, c, al=255):
        for j in range(h):
            for i in range(w):
                self.set(x + i, y + j, c, al)

    def disc(self, cx, cy, r, c, al=255):
        for y in range(int(cy - r - 1), int(cy + r + 2)):
            for x in range(int(cx - r - 1), int(cx + r + 2)):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                    self.set(x, y, c, al)

    def ell(self, cx, cy, rx, ry, c, al=255, rnd=None):
        for y in range(int(cy - ry - 1), int(cy + ry + 2)):
            for x in range(int(cx - rx - 1), int(cx + rx + 2)):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                    col = c
                    if rnd:
                        col = rnd()
                    self.set(x, y, col, al)

    def sky(self, c1, c2, dither=True):
        for y in range(self.h):
            t = y / (self.h - 1)
            for x in range(self.w):
                tt = t
                if dither:
                    tt += (((x + y) % 2) * 0.03 - 0.015)
                self.set(x, y, lerp(c1, c2, max(0, min(1, tt))))

    def hills(self, base, amp, top, dirt, speck=None, ph=0.0, rough=0.0):
        """Chão ondulado: topo (grama) + terra. Retorna a lista das alturas do topo."""
        tops = []
        for x in range(self.w):
            y0 = base + int(round(math.sin(x * 0.11 + ph) * amp + math.sin(x * 0.043 + ph * 2) * amp * 0.9))
            tops.append(y0)
            for y in range(y0, self.h):
                if y == y0:
                    self.set(x, y, top)
                elif y == y0 + 1 and rough:
                    self.set(x, y, lerp(top, dirt, 0.5))
                else:
                    c = dirt
                    if speck and self.r.random() < 0.12:
                        c = speck
                    elif self.r.random() < 0.18:
                        c = shade(dirt, 0.9)
                    self.set(x, y, c)
        return tops

    def cloud(self, x, y, w=10, c=(255, 255, 255), al=235):
        self.rect(x + 2, y, w - 4, 1, c, al)
        self.rect(x, y + 1, w, 1, c, al)
        self.rect(x + 1, y + 2, w - 3, 1, shade(c, 0.93), al)

    def tree(self, x, ground_y, h, trunk, crown_cols, cw=7, ch=5, wide=1):
        for j in range(h):
            for i in range(wide):
                self.set(x + i, ground_y - 1 - j, trunk if (j + i) % 3 else shade(trunk, 0.85))
        cx = x + wide / 2.0
        cy = ground_y - h - ch / 2.0 + 1
        for yy in range(int(cy - ch / 2) - 1, int(cy + ch / 2) + 2):
            for xx in range(int(cx - cw / 2) - 1, int(cx + cw / 2) + 2):
                if ((xx + 0.5 - cx) / (cw / 2.0)) ** 2 + ((yy + 0.5 - cy) / (ch / 2.0)) ** 2 <= 1.0:
                    self.set(xx, yy, self.r.choice(crown_cols))

    def stars(self, n, y_max, cols=((255, 255, 255), (220, 220, 255))):
        for _ in range(n):
            self.set(self.r.randrange(self.w), self.r.randrange(y_max), self.r.choice(cols), self.r.choice([255, 200, 140]))

    def noise_dots(self, n, cols, y0=0, y1=None, x0=0, x1=None, al=255):
        y1 = y1 or self.h
        x1 = x1 or self.w
        for _ in range(n):
            self.set(self.r.randrange(x0, x1), self.r.randrange(y0, y1), self.r.choice(cols), al)

    def save(self, path, scale=1):
        im = Image.fromarray(self.a, 'RGBA')
        if scale != 1:
            im = im.resize((self.w * scale, self.h * scale), Image.NEAREST)
        im.save(path)


# ---------------------------------------------------------------- cenas

def vignette(g, strength=0.22):
    for y in range(g.h):
        for x in range(g.w):
            d = abs(x - g.w / 2) / (g.w / 2)
            if d > 0.6:
                f = 1 - (d - 0.6) / 0.4 * strength
                g.a[y, x, :3] = (g.a[y, x, :3] * f).astype(np.uint8)


def scene_end(g):
    g.sky(hexc('#07030F'), hexc('#2A1250'))
    g.stars(70, g.h, ((255, 255, 255), (200, 170, 255), (255, 200, 255)))
    # ilha flutuante
    for x in range(8, 88):
        d = abs(x - 48) / 40.0
        top = 17 + int(d * 3)
        bot = 24 - int(d ** 0.7 * 7) + 4
        for y in range(top, min(g.h, 24 - 0)):
            if y < top + 1:
                g.set(x, y, hexc('#F1EDB0'))
            elif y > g.h - 1 - int(d * 9 + 1):
                continue
            else:
                g.set(x, y, g.r.choice([hexc('#E3DE9A'), hexc('#D4CE86'), hexc('#CFC87E')]))
    # pilares de obsidiana
    for px, ph in ((22, 10), (40, 13), (60, 11), (76, 8)):
        top = 17 - ph + (abs(px - 48) // 14)
        g.rect(px, top, 3, 17 + (abs(px - 48) // 14) - top, hexc('#1A0F29'))
        g.rect(px, top, 1, 17 + (abs(px - 48) // 14) - top, hexc('#2E1B47'))
    # cristais do Fim
    for px, py in ((23, 6), (61, 8)):
        g.set(px, py, hexc('#FF5CF4'))
        g.set(px - 1, py + 1, hexc('#FF9BF8'))
        g.set(px + 1, py + 1, hexc('#FF9BF8'))
        g.set(px, py + 2, hexc('#C21FBF'))
        g.disc(px, py + 1, 3, hexc('#FF5CF4'), 40)
    # dragão (silhueta)
    for i, (dx, dy) in enumerate([(0, 0), (1, 0), (2, 0), (3, -1), (4, -1), (5, -2), (6, -2), (7, -1), (8, -1), (9, 0), (10, 0), (11, 0), (12, 1)]):
        g.set(70 + dx - 6, 4 + dy, hexc('#241338'))
        if 2 <= i <= 10:
            g.set(70 + dx - 6, 5 + dy, hexc('#241338'))
    g.set(81 - 6, 3, hexc('#E040FB'))
    g.set(70 - 6 + 4, 2, hexc('#241338')); g.set(70 - 6 + 3, 1, hexc('#241338'))
    g.set(70 - 6 + 5, 2, hexc('#241338')); g.set(70 - 6 + 6, 1, hexc('#241338'))
    vignette(g)


def scene_jungle(g):
    g.sky(hexc('#7FD6A8'), hexc('#DDF5C9'))
    g.disc(78, 5, 3, hexc('#FFF9B0'))
    tops = g.hills(18, 1, hexc('#3FA33A'), hexc('#6B4A2A'), hexc('#7A5A34'))
    greens = [hexc('#1F7A2A'), hexc('#2E9A35'), hexc('#3FB447'), hexc('#18671F')]
    for x in range(4, 96, 11):
        h = g.r.randrange(8, 13)
        g.tree(x, tops[x] + 1, h, hexc('#6E4F2B'), greens, cw=11, ch=6, wide=2)
    # cipós e folhas
    for x in range(0, 96, 5):
        L = g.r.randrange(2, 7)
        for j in range(L):
            g.set(x, j, hexc('#1F7A2A'))
    # ocelote (manchas)
    g.rect(40, 16, 5, 2, hexc('#E8B84A')); g.set(41, 16, hexc('#5A3A12')); g.set(43, 16, hexc('#5A3A12')); g.set(45, 15, hexc('#E8B84A'))
    vignette(g)


def scene_village(g):
    g.sky(hexc('#6EC1FF'), hexc('#D8F1FF'))
    g.cloud(8, 3, 11); g.cloud(60, 5, 13); g.cloud(80, 2, 9)
    g.hills(19, 1, hexc('#58B040'), hexc('#7A5632'), hexc('#8A643A'))
    for hx, hw in ((14, 14), (44, 18), (74, 12)):
        wall = g.r.choice([hexc('#B8874A'), hexc('#C69A5C')])
        wh = 6
        by = 19
        g.rect(hx, by - wh, hw, wh + 1, wall)
        for i in range(0, hw, 2):
            g.set(hx + i, by - wh, shade(wall, 0.85))
        # telhado em degraus
        for k in range(0, hw // 2 + 1):
            g.rect(hx - 1 + k, by - wh - 1 - k, hw + 2 - 2 * k, 1, hexc('#7A4A2A') if k % 2 else hexc('#8A5A34'))
            if hw + 2 - 2 * k <= 0:
                break
        g.rect(hx + hw // 2 - 1, by - 3, 2, 4, hexc('#4A2F18'))
        g.rect(hx + 2, by - 4, 2, 2, hexc('#BEE9FF'))
        if hw > 14:
            g.rect(hx + hw - 5, by - 4, 2, 2, hexc('#BEE9FF'))
    # caminho
    for x in range(g.w):
        g.set(x, 21, hexc('#C9AC6C')); g.set(x, 22, hexc('#B99A5A'))
    # sino
    g.rect(60, 12, 1, 6, hexc('#6B4A2A')); g.set(60, 11, hexc('#FFD24A')); g.set(61, 11, hexc('#FFD24A'))
    vignette(g, 0.15)


def scene_night(g):
    g.sky(hexc('#0B1030'), hexc('#2B2A5A'))
    g.stars(55, 14)
    g.disc(76, 6, 4, hexc('#FFF4C2'))
    g.disc(77, 5, 1, hexc('#E7D79C')); g.disc(75, 8, 1, hexc('#E7D79C'))
    g.disc(76, 6, 7, hexc('#FFF4C2'), 25)
    tops = g.hills(19, 1, hexc('#1E3A2A'), hexc('#18212A'), hexc('#223040'))
    # casa assombrada
    g.rect(14, 12, 14, 8, hexc('#14141F'))
    for k in range(7):
        g.rect(13 + k, 11 - k, 16 - 2 * k, 1, hexc('#0C0C14'))
    g.rect(17, 14, 2, 2, hexc('#FFB020')); g.rect(23, 14, 2, 2, hexc('#FFB020'))
    # árvores secas
    for x in (40, 54, 88):
        for j in range(8):
            g.set(x, 18 - j, hexc('#0D0D15'))
        g.set(x - 1, 12, hexc('#0D0D15')); g.set(x + 1, 11, hexc('#0D0D15')); g.set(x - 2, 11, hexc('#0D0D15'))
    # olhos vermelhos
    g.set(46, 17, hexc('#FF2020')); g.set(48, 17, hexc('#FF2020'))
    g.set(66, 18, hexc('#FF2020')); g.set(68, 18, hexc('#FF2020'))
    # bruxa/chapéu? morcegos
    for bx, by in ((34, 5), (60, 3), (82, 12)):
        g.set(bx, by, hexc('#0A0A12')); g.set(bx - 1, by - 1, hexc('#0A0A12')); g.set(bx + 1, by - 1, hexc('#0A0A12'))
        g.set(bx - 2, by, hexc('#0A0A12')); g.set(bx + 2, by, hexc('#0A0A12'))
    vignette(g, 0.3)


def scene_redstone(g):
    g.sky(hexc('#2A2A2E'), hexc('#161618'), dither=False)
    # textura de pedra
    for _ in range(300):
        g.set(g.r.randrange(g.w), g.r.randrange(g.h), g.r.choice([hexc('#333338'), hexc('#202024'), hexc('#2E2E33')]))
    red_on, red_off = hexc('#FF3B30'), hexc('#7A1510')
    # circuito
    path = [(0, 18), (30, 18), (30, 8), (52, 8), (52, 16), (70, 16), (70, 5), (95, 5)]
    for (x0, y0), (x1, y1) in zip(path, path[1:]):
        if x0 == x1:
            for y in range(min(y0, y1), max(y0, y1) + 1):
                g.set(x0, y, red_on if (y // 3) % 3 else red_off)
                g.set(x0 - 1, y, shade(red_off, 0.7), 140)
        else:
            for x in range(min(x0, x1), max(x0, x1) + 1):
                g.set(x, y0, red_on if (x // 3) % 3 else red_off)
                g.set(x, y0 + 1, shade(red_off, 0.7), 140)
    # tochas
    for tx, ty in ((20, 14), (44, 5), (62, 12)):
        g.rect(tx, ty + 1, 1, 3, hexc('#6B4A2A'))
        g.set(tx, ty, hexc('#FF5A4A')); g.disc(tx, ty, 3, hexc('#FF3B30'), 45)
    # lâmpada
    g.rect(82, 9, 6, 6, hexc('#FFD66B')); g.rect(83, 10, 4, 4, hexc('#FFF1B8'))
    g.disc(85, 12, 8, hexc('#FFD66B'), 30)
    # pistão
    g.rect(10, 5, 8, 4, hexc('#8B6B3C')); g.rect(10, 5, 3, 4, hexc('#B4B4B4'))
    g.rect(13, 6, 5, 2, hexc('#C9A66B'))
    vignette(g)


def scene_plains(g, meadow=False):
    if meadow:
        g.sky(hexc('#FFB88A'), hexc('#FFE9C8'))
        g.disc(20, 17, 6, hexc('#FFF2B0'), 190)
    else:
        g.sky(hexc('#5DB8FF'), hexc('#CFEFFF'))
        g.disc(80, 5, 3, hexc('#FFF4B0'))
    g.cloud(10, 3, 12); g.cloud(52, 6, 10)
    tops = g.hills(17, 2, hexc('#62B84A'), hexc('#7A5632'), hexc('#8A643A'), ph=1.0 if meadow else 0.2)
    # flores
    cols = [hexc('#FF5A5A'), hexc('#FFE14A'), hexc('#8AC8FF'), hexc('#FFFFFF'), hexc('#FF9BD8')]
    for x in range(1, 96, 3):
        if g.r.random() < (0.8 if meadow else 0.35):
            y = tops[x]
            g.set(x, y - 1, g.r.choice(cols))
            g.set(x, y, hexc('#3C8A30'))
    if not meadow:
        # cavalo (bloco)
        hx, hy = 62, tops[62] - 1
        g.rect(hx, hy - 4, 9, 3, hexc('#8B5A2B'))
        g.rect(hx + 8, hy - 7, 3, 4, hexc('#8B5A2B'))
        g.rect(hx + 10, hy - 6, 2, 2, hexc('#5E3A18'))
        g.rect(hx, hy - 1, 1, 2, hexc('#5E3A18')); g.rect(hx + 2, hy - 1, 1, 2, hexc('#5E3A18'))
        g.rect(hx + 6, hy - 1, 1, 2, hexc('#5E3A18')); g.rect(hx + 8, hy - 1, 1, 2, hexc('#5E3A18'))
        g.set(hx + 9, hy - 6, hexc('#111111')); g.rect(hx + 7, hy - 8, 2, 3, hexc('#2A1A0A'))
        g.set(hx - 1, hy - 4, hexc('#2A1A0A')); g.set(hx - 1, hy - 3, hexc('#2A1A0A'))
        # palheiro
        g.rect(20, tops[20] - 4, 6, 4, hexc('#E0C050')); g.rect(20, tops[20] - 3, 6, 1, hexc('#B89A30'))
    else:
        # mochilas/bundle
        for bx in (30, 50, 70):
            y = tops[bx]
            g.rect(bx, y - 5, 5, 5, hexc('#B07A4A')); g.rect(bx, y - 5, 5, 1, hexc('#8A5A30'))
            g.rect(bx + 1, y - 3, 3, 1, hexc('#E8C890'))
            g.rect(bx + 2, y - 6, 1, 1, hexc('#8A5A30'))
    vignette(g, 0.15)


def scene_mesa(g):
    g.sky(hexc('#F7C08A'), hexc('#FFE7C4'))
    g.disc(70, 6, 4, hexc('#FFF4C0'))
    bands = [hexc('#D2693C'), hexc('#EDE0C4'), hexc('#B8512A'), hexc('#F2C58C'), hexc('#9A3F1E'), hexc('#D98F55')]
    # mesas
    for mx, mw, mh in ((6, 28, 11), (46, 20, 8), (74, 18, 13)):
        for y in range(mh):
            yy = 24 - mh + y
            c = bands[(y // 2) % len(bands)]
            for x in range(mw):
                edge = 1 if (x < 1 or x > mw - 2) else 0
                g.set(mx + x, yy, shade(c, 0.88 if edge else 1.0))
    for x in range(g.w):
        for y in range(21, 24):
            g.set(x, y, hexc('#C8844C') if (x + y) % 3 else hexc('#B87440'))
    # cacto
    for cx in (40, 68):
        g.rect(cx, 15, 2, 6, hexc('#3F8A3F')); g.rect(cx - 2, 17, 2, 1, hexc('#3F8A3F')); g.rect(cx - 2, 15, 1, 3, hexc('#3F8A3F'))
    vignette(g, 0.18)


def scene_ocean(g):
    g.sky(hexc('#43B5F2'), hexc('#06407A'))
    # superfície
    for x in range(g.w):
        g.set(x, 1, hexc('#BDEBFF'), 200 if x % 4 else 120)
    for x in range(0, g.w, 7):
        g.rect(x, 0, 3, 1, hexc('#E4F8FF'), 180)
    # raios de luz
    for rx in (14, 40, 70):
        for y in range(2, 20):
            for k in range(3 + y // 5):
                g.set(rx + y // 3 + k, y, hexc('#CFF3FF'), 22)
    # areia
    for x in range(g.w):
        y0 = 20 + int(round(math.sin(x * 0.2) * 0.8))
        for y in range(y0, g.h):
            g.set(x, y, g.r.choice([hexc('#E8D9A0'), hexc('#DCCB8E'), hexc('#CDBB7C')]))
    # coral
    for cx, cc in ((18, '#FF5DA2'), (24, '#B14DFF'), (66, '#FF7A3D'), (72, '#4DD2FF')):
        for k in range(5):
            g.set(cx, 19 - k, hexc(cc)); 
            if k % 2: g.set(cx - 1, 19 - k, hexc(cc)); g.set(cx + 1, 19 - k, hexc(cc))
    # algas
    for kx in (34, 38, 52, 84, 88):
        for j in range(9):
            g.set(kx + int(round(math.sin(j * 0.9 + kx))), 20 - j, hexc('#2E9A4F'))
    # monumento (prismarina)
    for y in range(6):
        w = 12 - y * 2
        g.rect(46 - w // 2 + 4, 14 + y, max(2, w), 1, hexc('#4AA79A') if y % 2 else hexc('#62C7B5'))
    g.rect(45, 12, 2, 2, hexc('#E8C34A'))
    # golfinho
    for i, (dx, dy) in enumerate([(0, 0), (1, 0), (2, -1), (3, -1), (4, -1), (5, 0), (6, 0), (7, 1), (8, 2), (9, 2)]):
        g.set(80 + dx - 66, 7 + dy + 0, hexc('#8FA6BA')); g.set(80 + dx - 66, 8 + dy + 0, hexc('#C8D6E2'))
    g.set(80 - 66 + 3, 6, hexc('#8FA6BA'))
    # peixes
    for fx, fy, fc in ((30, 9, '#FF8A3D'), (58, 12, '#FFE04A'), (86, 8, '#FF5D8A')):
        g.rect(fx, fy, 3, 2, hexc(fc)); g.set(fx + 3, fy - 1, hexc(fc)); g.set(fx + 3, fy + 2, hexc(fc)); g.set(fx + 1, fy, (20, 20, 20))
    # bolhas
    for _ in range(14):
        g.set(g.r.randrange(g.w), g.r.randrange(3, 19), hexc('#D8F6FF'), 170)
    vignette(g)


def scene_frost(g):
    g.sky(hexc('#9FD4F5'), hexc('#EAF7FF'))
    g.disc(78, 5, 3, hexc('#FFFFFF'), 220)
    tops = g.hills(18, 1.4, hexc('#FFFFFF'), hexc('#DDEBF5'), hexc('#CBE0EE'))
    # espinhos de gelo
    for x, h in ((12, 9), (30, 12), (68, 10), (86, 7)):
        for j in range(h):
            w = max(1, (h - j) // 3 + 1)
            g.rect(x - w // 2, 20 - j, w, 1, hexc('#8FD8F5') if j % 2 else hexc('#B6EBFF'))
    # abetos
    for x in (22, 46, 58, 78):
        base = tops[x]
        for k in range(4):
            w = 3 + k * 2
            g.rect(x - w // 2, base - 9 + k * 2, w, 2, hexc('#1F5E3A'))
            g.rect(x - w // 2, base - 9 + k * 2, w, 1, hexc('#F4FAFF'))
        g.rect(x, base - 1, 1, 2, hexc('#4A2F18'))
    # urso polar
    g.rect(36, 17, 7, 3, hexc('#FFFFFF')); g.rect(41, 15, 3, 3, hexc('#FFFFFF')); g.set(43, 16, (20, 20, 20))
    g.rect(36, 19, 1, 2, hexc('#E4EEF5')); g.rect(41, 19, 1, 2, hexc('#E4EEF5'))
    # neve caindo
    for _ in range(45):
        g.set(g.r.randrange(g.w), g.r.randrange(g.h), hexc('#FFFFFF'), 220)
    vignette(g, 0.12)


def scene_mansion(g):
    g.sky(hexc('#2B3D2E'), hexc('#5C7A55'))
    # floresta escura
    tops = g.hills(19, 0.8, hexc('#1F4A25'), hexc('#2A1E12'), hexc('#3A2A18'))
    dark = [hexc('#0F3A1A'), hexc('#154A22'), hexc('#0B2C14'), hexc('#1B5A2A')]
    for x in range(-2, 98, 9):
        h = g.r.randrange(8, 13)
        g.tree(x, tops[max(0, min(95, x))] + 1, h, hexc('#3A2412'), dark, cw=12, ch=7, wide=2)
    # mansão
    g.rect(38, 8, 22, 11, hexc('#5A3A1E'))
    for i in range(0, 22, 2):
        g.set(38 + i, 8, hexc('#44290F'))
    for k in range(6):
        g.rect(36 + k * 2, 7 - k, 26 - k * 4, 1, hexc('#2A2A2A'))
    for wx in (41, 46, 52, 56):
        g.rect(wx, 11, 2, 3, hexc('#FFC84A'))
    g.rect(49, 15, 3, 5, hexc('#2A1608'))
    # totem / lhama? olhos
    g.set(25, 17, hexc('#7DFFB0')); g.set(27, 17, hexc('#7DFFB0'))
    vignette(g, 0.3)


def scene_color(g):
    g.sky(hexc('#FFF1F8'), hexc('#DDEBFF'))
    cols = ['#F9FFFE', '#F9801D', '#C74EBD', '#3AB3DA', '#FED83D', '#80C71F', '#F38BAA', '#474F52',
            '#9D9D97', '#169C9C', '#8932B8', '#3C44AA', '#835432', '#5E7C16', '#B02E26', '#1D1D21']
    # concreto em degraus (arco-íris)
    rb = ['#B02E26', '#F9801D', '#FED83D', '#80C71F', '#3AB3DA', '#3C44AA', '#8932B8']
    for i, c in enumerate(rb):
        for x in range(g.w):
            top = 6 + i * 2 + int(round(math.sin(x * 0.1) * 0.0))
            # arco
            cx = 48
            ry = 20 - i * 2
            rx = 46 - i * 3
            y = int(round(22 - ry * math.sqrt(max(0, 1 - ((x - cx) / rx) ** 2))))
            if abs(x - cx) <= rx:
                for yy in range(y, y + 2):
                    g.set(x, yy, hexc(c))
    # blocos coloridos no chão
    for i in range(0, g.w, 6):
        c = hexc(cols[(i // 6) % len(cols)])
        g.rect(i, 21, 6, 3, c); g.rect(i, 21, 6, 1, shade(c, 1.12)); g.rect(i + 5, 21, 1, 3, shade(c, 0.85))
    # papagaio
    g.rect(14, 9, 3, 3, hexc('#E53935')); g.rect(17, 10, 2, 1, hexc('#FFB300')); g.set(16, 9, (255, 255, 255)); g.rect(12, 12, 3, 2, hexc('#1E88E5'))
    g.rect(80, 7, 3, 3, hexc('#43A047')); g.rect(83, 8, 2, 1, hexc('#FFB300')); g.set(82, 7, (255, 255, 255)); g.rect(78, 10, 3, 2, hexc('#FDD835'))
    vignette(g, 0.1)


def scene_bees(g):
    g.sky(hexc('#FFD65A'), hexc('#FFF3C4'))
    g.disc(16, 5, 4, hexc('#FFFFFF'), 200)
    tops = g.hills(19, 1.2, hexc('#6CBF3F'), hexc('#7A5632'), hexc('#8A643A'))
    # colmeia / árvore
    g.rect(61, 8, 2, 12, hexc('#6B4A2A'))
    g.ell(62, 6, 8, 4, hexc('#4A9B35'), rnd=lambda: g.r.choice([hexc('#3F8A30'), hexc('#58AA3F'), hexc('#4A9B35')]))
    # favo de mel
    for i in range(3):
        for j in range(2):
            x, y = 58 + i * 3, 10 + j * 3
            g.rect(x, y, 3, 3, hexc('#F2A12A')); g.rect(x, y, 3, 1, hexc('#FFD24A'))
    g.rect(60, 13, 3, 2, hexc('#5A3A12'))
    # abelhas
    for bx, by in ((20, 7), (36, 11), (48, 5), (76, 9), (88, 13)):
        g.rect(bx, by, 3, 2, hexc('#FFD600')); g.set(bx + 1, by, (30, 30, 30)); g.set(bx + 1, by + 1, (30, 30, 30))
        g.set(bx + 3, by, (20, 20, 20)); g.set(bx, by - 1, (255, 255, 255)); g.set(bx + 1, by - 1, (230, 240, 255))
    # flores
    fc = [hexc('#FF6B9A'), hexc('#FFFFFF'), hexc('#B14DFF'), hexc('#FF9A3D'), hexc('#4DB8FF')]
    for x in range(2, 96, 4):
        y = tops[x]
        g.set(x, y - 1, g.r.choice(fc)); g.set(x, y - 2, g.r.choice(fc)) if g.r.random() < 0.4 else None
        g.set(x, y, hexc('#2F7A28'))
    vignette(g, 0.12)


def scene_nether(g):
    g.sky(hexc('#2A0000'), hexc('#8A1E0C'))
    # teto
    for x in range(g.w):
        L = 2 + int(abs(math.sin(x * 0.2) * 3) + g.r.random() * 2)
        for y in range(L):
            g.set(x, y, g.r.choice([hexc('#6B2424'), hexc('#7C2C2C'), hexc('#5A1D1D')]))
    # cachoeira de lava
    for lx in (14, 56, 80):
        for y in range(3, 18):
            g.set(lx, y, hexc('#FF8A1E')); g.set(lx + 1, y, hexc('#FFC84A') if y % 3 else hexc('#FF6A00'))
        g.disc(lx, 18, 3, hexc('#FF8A1E'), 70)
    # chão e lago
    for x in range(g.w):
        y0 = 16 + int(round(math.sin(x * 0.15) * 1.5))
        for y in range(y0, g.h):
            if 30 < x < 54 and y > y0 + 1:
                g.set(x, y, g.r.choice([hexc('#FF6A00'), hexc('#FF8A1E'), hexc('#FFB020'), hexc('#FF5A00')]))
            else:
                g.set(x, y, g.r.choice([hexc('#7A2A2A'), hexc('#8E3636'), hexc('#6A2222')]))
    # colunas de basalto
    for bx, bh in ((70, 9), (90, 7), (4, 8)):
        g.rect(bx, 17 - bh, 4, bh + 3, hexc('#3A3A44'))
        g.rect(bx, 17 - bh, 1, bh + 3, hexc('#2A2A32')); g.rect(bx + 3, 17 - bh, 1, bh + 3, hexc('#4C4C58'))
    # fungos
    for fx in (24, 64):
        g.rect(fx, 11, 1, 5, hexc('#2A8A8A')); g.rect(fx - 2, 9, 5, 2, hexc('#1FB0A5')); g.set(fx - 1, 9, hexc('#7CF0E0'))
    # brasas
    for _ in range(26):
        g.set(g.r.randrange(g.w), g.r.randrange(2, 18), hexc('#FFB020'), 200)
    vignette(g, 0.3)


def scene_caves(g):
    g.sky(hexc('#2D3038'), hexc('#10121A'), dither=False)
    for _ in range(260):
        g.set(g.r.randrange(g.w), g.r.randrange(g.h), g.r.choice([hexc('#3A3D46'), hexc('#262830'), hexc('#34363E')]))
    # estalactites
    for x in range(0, g.w, 1):
        pass
    for x, L in ((6, 8), (14, 5), (30, 9), (44, 6), (58, 10), (72, 5), (86, 8)):
        for j in range(L):
            w = max(1, (L - j) // 3 + 1)
            g.rect(x - w // 2, j, w, 1, hexc('#4B4F5A') if j % 2 else hexc('#585C68'))
    # estalagmites
    for x, L in ((20, 6), (50, 8), (66, 5), (80, 7)):
        for j in range(L):
            w = max(1, (L - j) // 3 + 1)
            g.rect(x - w // 2, 23 - j, w, 1, hexc('#4B4F5A') if j % 2 else hexc('#585C68'))
    # ametista
    for ax, ay in ((36, 19), (40, 17), (42, 20), (78, 18)):
        for k in range(4):
            g.set(ax, ay - k, hexc('#B388FF') if k % 2 else hexc('#7E57C2'))
        g.set(ax - 1, ay - 1, hexc('#7E57C2')); g.set(ax + 1, ay - 2, hexc('#D1B3FF'))
        g.disc(ax, ay - 2, 4, hexc('#B388FF'), 35)
    # minérios
    for ox, oy, c in ((10, 18, '#46E8F0'), (24, 13, '#E8803A'), (60, 16, '#F5E64A'), (90, 14, '#46E8F0'), (52, 5, '#E8803A')):
        g.rect(ox, oy, 2, 2, hexc(c)); g.set(ox + 2, oy, hexc(c))
    # liquens brilhantes
    g.noise_dots(18, [hexc('#7DFFB0'), hexc('#B6FFD0')], 0, 24)
    # lago
    g.rect(22, 21, 20, 3, hexc('#2A6FD8'), 220); g.rect(22, 21, 20, 1, hexc('#6FA8FF'), 220)
    # axolote
    g.rect(26, 20, 4, 2, hexc('#FF9ACF')); g.set(30, 20, hexc('#FFB6DD')); g.set(25, 19, hexc('#FF6FB5')); g.set(25, 21, hexc('#FF6FB5'))
    vignette(g, 0.35)


def scene_mountain(g):
    g.sky(hexc('#74C0FF'), hexc('#DDF1FF'))
    g.cloud(6, 3, 12); g.cloud(70, 5, 14)
    peaks = [(14, 18), (34, 21), (60, 19), (82, 16)]
    for x in range(g.w):
        h = 0
        for px, ph in peaks:
            h = max(h, ph - abs(x - px) * 1.1)
        top = int(24 - h)
        for y in range(top, g.h):
            snow = y < top + max(1, (24 - top) // 3)
            c = hexc('#FFFFFF') if snow else (hexc('#7E8794') if (x + y) % 5 else hexc('#6E7784'))
            if not snow and y == top + (24 - top) // 3 and x % 2: c = hexc('#FFFFFF')
            g.set(x, y, c)
    for x in range(g.w):
        for y in range(21, g.h):
            g.set(x, y, hexc('#4C9A3A') if y == 21 else hexc('#6F5A3E'))
    # cabras
    g.rect(30, 17, 3, 2, hexc('#E8E2D6')); g.set(33, 16, hexc('#E8E2D6')); g.set(33, 15, hexc('#B8A98A')); g.rect(30, 19, 1, 2, hexc('#8A7E68')); g.rect(32, 19, 1, 2, hexc('#8A7E68'))
    vignette(g, 0.12)


def scene_wild(g):
    g.sky(hexc('#2E5E57'), hexc('#8FC9A0'))
    g.noise_dots(22, [hexc('#FFE88A'), hexc('#C9FF8A')], 0, 16, al=200)  # vagalumes
    # água do pântano
    for x in range(g.w):
        for y in range(19, g.h):
            g.set(x, y, hexc('#3F7A66') if (x + y) % 3 else hexc('#4A8A74'))
    # mangues com raízes
    for x in (16, 42, 70):
        for j in range(7):
            g.set(x, 18 - j, hexc('#5A3A22')); g.set(x + 1, 18 - j, hexc('#6A4A2E'))
        for r in range(-3, 5):
            if r in (0, 1): continue
            for k in range(abs(r) + 1):
                g.set(x + r, 18 + (0 if k < 1 else 0) - max(0, 3 - abs(r)) + (1 if abs(r) > 1 else 0) - 0, hexc('#5A3A22'))
        g.ell(x + 0.5, 8, 8, 4, hexc('#3F8A3F'), rnd=lambda: g.r.choice([hexc('#2E7A35'), hexc('#3F8A3F'), hexc('#4EA04A')]))
    # sapo
    g.rect(56, 17, 3, 2, hexc('#E8A23A')); g.set(56, 16, (255, 255, 255)); g.set(58, 16, (255, 255, 255))
    # olhos do warden / deep dark (brilho azul)
    for sx in (86, 90):
        g.rect(sx, 20, 1, 3, hexc('#0B3A4A')); g.set(sx, 20, hexc('#2DD4E8'))
    g.disc(88, 21, 5, hexc('#2DD4E8'), 25)
    # allay
    g.rect(30, 5, 2, 2, hexc('#6FC8FF')); g.set(29, 5, hexc('#BEE8FF')); g.set(32, 5, hexc('#BEE8FF')); g.disc(31, 6, 4, hexc('#6FC8FF'), 40)
    vignette(g, 0.25)


def scene_cherry(g):
    g.sky(hexc('#FFC4DD'), hexc('#FFF0F6'))
    g.disc(80, 5, 3, hexc('#FFFFFF'), 220)
    g.cloud(8, 3, 11, c=(255, 255, 255)); g.cloud(52, 2, 9)
    tops = g.hills(19, 1.3, hexc('#8FCB5A'), hexc('#7A5632'), hexc('#8A643A'))
    pinks = [hexc('#F7A6C8'), hexc('#FFC8DE'), hexc('#EE8BB5'), hexc('#FFD9E8'), hexc('#F9B5D2')]
    for x, h, cw in ((12, 9, 15), (40, 11, 17), (70, 9, 15), (90, 7, 11)):
        base = tops[min(x, 95)] + 1
        for j in range(h):
            g.set(x, base - 1 - j, hexc('#4D3326')); g.set(x + 1, base - 1 - j, hexc('#5E3F2E'))
        g.set(x - 1, base - h + 1, hexc('#4D3326')); g.set(x + 2, base - h + 2, hexc('#4D3326'))
        g.ell(x + 1, base - h - 2, cw / 2, 4.6, hexc('#F7A6C8'), rnd=lambda: g.r.choice(pinks))
    # pétalas no ar e no chão
    for _ in range(46):
        x, y = g.r.randrange(g.w), g.r.randrange(2, 20)
        g.set(x, y, g.r.choice(pinks))
    for x in range(g.w):
        if g.r.random() < 0.35:
            g.set(x, tops[x], g.r.choice(pinks))
    # camelo
    cx, cy = 26, tops[26] - 1
    g.rect(cx, cy - 4, 8, 3, hexc('#D9A857')); g.rect(cx + 2, cy - 6, 2, 2, hexc('#C8963F')); g.rect(cx + 7, cy - 7, 2, 4, hexc('#D9A857')); g.rect(cx + 8, cy - 8, 2, 2, hexc('#D9A857'))
    for lx in (0, 2, 5, 7):
        g.rect(cx + lx, cy - 1, 1, 2, hexc('#B8873A'))
    g.set(cx + 9, cy - 8, (20, 20, 20))
    vignette(g, 0.12)


def scene_savanna(g):
    g.sky(hexc('#FF9A4A'), hexc('#FFE2A8'))
    g.disc(70, 12, 6, hexc('#FFF0B0'), 200)
    tops = g.hills(19, 1.2, hexc('#C9B24A'), hexc('#8A5A2E'), hexc('#9A6A38'))
    for x in (12, 44, 82):
        base = tops[x]
        g.rect(x, base - 7, 1, 7, hexc('#4A3220'))
        for k in range(-5, 6):
            g.set(x + k, base - 8 - (1 if abs(k) < 3 else 0), hexc('#5E7A2A'))
        g.set(x - 3, base - 9, hexc('#5E7A2A'))
    # lobo com armadura
    wx, wy = 28, tops[28] - 1
    g.rect(wx, wy - 3, 6, 2, hexc('#C8C8C8')); g.rect(wx + 5, wy - 4, 3, 3, hexc('#D8D8D8')); g.set(wx + 7, wy - 3, (20, 20, 20))
    g.rect(wx + 1, wy - 4, 3, 2, hexc('#B8B8C8')); g.rect(wx + 1, wy - 4, 3, 1, hexc('#E8E8F8'))  # armadura
    g.rect(wx, wy - 1, 1, 2, hexc('#B0B0B0')); g.rect(wx + 4, wy - 1, 1, 2, hexc('#B0B0B0'))
    # tatu
    g.rect(58, tops[58] - 3, 5, 3, hexc('#B07A5A')); g.rect(58, tops[58] - 3, 5, 1, hexc('#8A5A40')); g.set(63, tops[58] - 2, hexc('#D9A58A'))
    vignette(g, 0.2)


def scene_trials(g):
    g.sky(hexc('#33363B'), hexc('#1C1E22'), dither=False)
    # parede de tuff
    for y in range(g.h):
        for x in range(g.w):
            c = hexc('#4A4D52') if ((x // 6 + (y // 4) * 2) % 2) else hexc('#42454A')
            if x % 6 == 0 or y % 4 == 0: c = hexc('#2E3034')
            g.set(x, y, c)
    cop = [hexc('#C8602E'), hexc('#E37B3F'), hexc('#E9A06A')]
    ox = [hexc('#3FB59B'), hexc('#5BD4B6'), hexc('#2E8E7A')]
    # blocos de cobre (oxidados da esq p/ dir)
    for i in range(0, 8):
        x0 = 6 + i * 11
        c = cop if i < 3 else ox
        base = c[0]
        g.rect(x0, 12, 8, 8, base)
        g.rect(x0, 12, 8, 1, c[2]); g.rect(x0, 12, 1, 8, c[1]); g.rect(x0 + 7, 12, 1, 8, shade(base, 0.8)); g.rect(x0, 19, 8, 1, shade(base, 0.8))
        g.rect(x0 + 2, 14, 4, 4, shade(base, 0.85)); g.rect(x0 + 3, 15, 2, 2, c[1])
    # spawner
    g.rect(44, 3, 8, 8, hexc('#111317')); 
    for k in range(0, 8, 2):
        g.rect(44 + k, 3, 1, 8, hexc('#2A2E36'))
    g.rect(46, 5, 4, 4, hexc('#5AB6FF'), 200); g.disc(48, 7, 6, hexc('#5AB6FF'), 40)
    # breeze (redemoinho)
    for k in range(14):
        a = k * 0.9
        g.set(80 + math.cos(a) * (2 + k * 0.4), 14 + math.sin(a) * (1 + k * 0.3) - k * 0.5, hexc('#CFE8FF'))
    g.rect(10, 4, 3, 3, hexc('#E7D26A')); g.rect(11, 5, 1, 1, hexc('#8A6A18'))
    vignette(g, 0.3)


def scene_pale(g):
    g.sky(hexc('#B9BFC4'), hexc('#8F979D'))
    # neblina
    for y in range(6, 18):
        for x in range(g.w):
            if (x * 7 + y * 3) % 11 < 4:
                g.set(x, y, hexc('#D8DDE0'), 40)
    tops = g.hills(20, 0.8, hexc('#7C8A72'), hexc('#4B4A44'), hexc('#5A584F'))
    for x, h in ((10, 13), (30, 11), (54, 14), (76, 12)):
        base = tops[x]
        for j in range(h):
            g.set(x, base - 1 - j, hexc('#D6D1C6')); g.set(x + 1, base - 1 - j, hexc('#C3BDB0')); g.set(x + 2, base - 1 - j, hexc('#B0AA9C'))
        # galhos e copa rala
        for k in range(1, 6):
            g.set(x - k, base - h + 1 + k // 2, hexc('#B0AA9C')); g.set(x + 2 + k, base - h + 1 + k // 2, hexc('#B0AA9C'))
        g.ell(x + 1, base - h - 1, 8, 3.2, hexc('#6E7A66'), rnd=lambda: g.r.choice([hexc('#6E7A66'), hexc('#5A6654'), hexc('#7E8A76')]))
    # creaking (olhos laranja)
    g.rect(64, 15, 5, 5, hexc('#5A5750')); g.set(65, 16, hexc('#FF7A1A')); g.set(67, 16, hexc('#FF7A1A')); g.disc(66, 16, 4, hexc('#FF7A1A'), 40)
    # resina
    for rx in (22, 44, 86):
        g.set(rx, 17, hexc('#E8731A')); g.set(rx, 18, hexc('#C9560F'))
    vignette(g, 0.3)


def scene_spring(g):
    g.sky(hexc('#8ED8FF'), hexc('#E8FFF0'))
    g.disc(14, 5, 3, hexc('#FFF4B0'))
    g.cloud(40, 3, 12); g.cloud(76, 6, 10)
    tops = g.hills(18, 2, hexc('#6CD04A'), hexc('#7A5632'), hexc('#8A643A'), ph=2.0)
    # folhas caídas e flores
    leaf = [hexc('#C8872A'), hexc('#A8661A'), hexc('#E0A83A'), hexc('#8A9A2A')]
    for x in range(g.w):
        if g.r.random() < 0.55:
            g.set(x, tops[x] - 1, g.r.choice(leaf))
    flo = [hexc('#FF6BA8'), hexc('#FFE14A'), hexc('#FFFFFF'), hexc('#9A6BFF')]
    for x in range(2, 96, 3):
        if g.r.random() < 0.6:
            g.set(x, tops[x] - 2, g.r.choice(flo)); g.set(x, tops[x] - 1, hexc('#3C9A30'))
    # arbusto de vagalumes
    for bx in (30, 70):
        g.ell(bx, tops[bx] - 2, 4, 2.4, hexc('#2E8A3A'), rnd=lambda: g.r.choice([hexc('#2E8A3A'), hexc('#3FA048')]))
        g.set(bx - 1, tops[bx] - 3, hexc('#F7FF8A')); g.set(bx + 2, tops[bx] - 4, hexc('#F7FF8A'))
        g.disc(bx, tops[bx] - 3, 5, hexc('#F7FF8A'), 25)
    # porco / vaca variantes
    px, py = 50, tops[50] - 1
    g.rect(px, py - 3, 6, 3, hexc('#E8B0A0')); g.rect(px + 5, py - 4, 3, 3, hexc('#E8B0A0')); g.set(px + 7, py - 3, hexc('#C8887A')); g.rect(px, py - 1, 1, 2, hexc('#C8887A')); g.rect(px + 4, py - 1, 1, 2, hexc('#C8887A'))
    vignette(g, 0.1)


def scene_sky(g):
    g.sky(hexc('#4DAEF5'), hexc('#CDEBFF'))
    for x, y, w in ((4, 15, 18), (30, 18, 22), (60, 16, 20), (80, 19, 16), (14, 4, 12), (70, 3, 14)):
        g.cloud(x, y, w)
        g.cloud(x + 2, y + 2, w - 4, c=(240, 246, 255))
    # happy ghast
    gx, gy = 38, 4
    g.rect(gx, gy, 18, 10, hexc('#F4F6FA'))
    g.rect(gx, gy, 18, 1, hexc('#FFFFFF')); g.rect(gx, gy + 9, 18, 1, hexc('#D6DBE6'))
    g.rect(gx + 17, gy, 1, 10, hexc('#D6DBE6'))
    for ex in (gx + 4, gx + 11):
        g.rect(ex, gy + 4, 3, 1, hexc('#3A3F55')); g.set(ex, gy + 3, hexc('#3A3F55')); g.set(ex + 2, gy + 3, hexc('#3A3F55'))
    g.rect(gx + 7, gy + 6, 4, 1, hexc('#E88AA8'))
    g.rect(gx + 2, gy + 6, 2, 1, hexc('#FFC8D8')); g.rect(gx + 14, gy + 6, 2, 1, hexc('#FFC8D8'))
    for tx in range(gx + 1, gx + 17, 3):
        g.rect(tx, gy + 10, 2, 5 + (tx % 3), hexc('#E4E8F0'))
    # arreio
    g.rect(gx + 2, gy + 1, 14, 1, hexc('#3B6EE8'))
    # jogador voando em cima
    g.rect(gx + 7, gy - 4, 3, 3, hexc('#C99A6A')); g.rect(gx + 7, gy - 1, 3, 1, hexc('#3AA0D8'))
    vignette(g, 0.1)


def scene_copper(g):
    g.sky(hexc('#1E3B3A'), hexc('#0E1F1F'), dither=False)
    cols = [('#C8602E', '#E37B3F', '#E9A06A'), ('#C8602E', '#E37B3F', '#E9A06A'), ('#B89A58', '#CDB070', '#E0C88C'),
            ('#6AA88A', '#7FC0A0', '#9CD8B8'), ('#3FB59B', '#5BD4B6', '#7FE8CC')]
    # parede de blocos oxidando
    for row in range(4):
        for i in range(0, 16):
            x0, y0 = i * 6, row * 6
            t = min(len(cols) - 1, int((i + row * 0.6) / 16 * len(cols) + g.r.random() * 0.6))
            base, mid, hi = [hexc(c) for c in cols[t]]
            g.rect(x0, y0, 6, 6, base)
            g.rect(x0, y0, 6, 1, hi); g.rect(x0, y0, 1, 6, mid)
            g.rect(x0 + 5, y0, 1, 6, shade(base, 0.75)); g.rect(x0, y0 + 5, 6, 1, shade(base, 0.75))
            g.rect(x0 + 2, y0 + 2, 2, 2, shade(base, 0.9))
    # golem de cobre
    gx, gy = 66, 8
    g.rect(gx, gy + 6, 8, 5, hexc('#D36A33')); g.rect(gx + 1, gy, 6, 6, hexc('#E37B3F')); g.rect(gx + 1, gy, 6, 1, hexc('#F2A06A'))
    g.set(gx + 2, gy + 2, (255, 255, 255)); g.set(gx + 5, gy + 2, (255, 255, 255)); g.set(gx + 2, gy + 2, (20, 20, 20)); g.set(gx + 5, gy + 2, (20, 20, 20))
    g.rect(gx + 3, gy - 3, 2, 3, hexc('#B8501F')); g.set(gx + 3, gy - 4, hexc('#FFD24A'))
    g.rect(gx - 3, gy + 6, 3, 2, hexc('#D36A33')); g.rect(gx + 8, gy + 6, 3, 2, hexc('#D36A33'))
    g.rect(gx + 1, gy + 11, 2, 3, hexc('#B8501F')); g.rect(gx + 5, gy + 11, 2, 3, hexc('#B8501F'))
    g.disc(gx + 4, gy + 5, 10, hexc('#FFB070'), 22)
    vignette(g, 0.25)


def scene_mounts(g):
    g.sky(hexc('#FF7A4A'), hexc('#FFD9A0'))
    g.disc(30, 14, 7, hexc('#FFF0B0'), 230)
    g.disc(30, 14, 11, hexc('#FFD07A'), 60)
    # dunas
    for layer, (base, col) in enumerate([(15, '#E8B66A'), (18, '#D9A258'), (21, '#C48C44')]):
        for x in range(g.w):
            y0 = base + int(round(math.sin(x * (0.07 + layer * 0.03) + layer * 2) * 2.2))
            for y in range(y0, g.h):
                g.set(x, y, hexc(col) if (x + y) % 7 else shade(hexc(col), 0.94))
    # pirâmide
    for k in range(7):
        g.rect(66 + k, 12 + k, 14 - 2 * k, 1, hexc('#E0B060') if k % 2 else hexc('#EBC474'))
    g.rect(66 + 6 - 0, 17, 3, 2, hexc('#6A4A22'))
    # camelo husk / montaria
    cx, cy = 44, 19
    g.rect(cx, cy - 4, 8, 3, hexc('#A88B55')); g.rect(cx + 2, cy - 6, 2, 2, hexc('#8A7040')); g.rect(cx + 7, cy - 7, 2, 4, hexc('#A88B55')); g.rect(cx + 8, cy - 8, 2, 2, hexc('#A88B55'))
    for lx in (0, 2, 5, 7): g.rect(cx + lx, cy - 1, 1, 2, hexc('#7A6238'))
    g.set(cx + 9, cy - 8, hexc('#7DFF7D'))
    # lança
    for k in range(10):
        g.set(8 + k, 9 - k // 2, hexc('#8A5A2E'))
    g.rect(18, 4, 3, 1, hexc('#D8D8E0'))
    vignette(g, 0.15)


DIGITS = {
    '2': ["111", "001", "111", "100", "111"],
    '6': ["111", "100", "111", "101", "111"],
}


def scene_year(g):
    g.sky(hexc('#1B1450'), hexc('#5E7CFF'))
    g.stars(60, 16)
    g.disc(84, 6, 4, hexc('#FFF4C2'))
    g.disc(85, 5, 4, hexc('#3B3A8A'), 0)
    # silhueta de montanhas
    for x in range(g.w):
        h = 5 + int(abs(math.sin(x * 0.09 + 1) * 5 + math.sin(x * 0.21) * 2))
        for y in range(24 - h, 24):
            g.set(x, y, hexc('#171044') if y > 24 - h + 1 else hexc('#2A2470'))
    # número 26 grande
    ox = 28
    for ch in '26':
        for r, row in enumerate(DIGITS[ch]):
            for c, v in enumerate(row):
                if v == '1':
                    g.rect(ox + c * 4, 4 + r * 3, 4, 3, hexc('#FFD54F'))
                    g.rect(ox + c * 4, 4 + r * 3, 4, 1, hexc('#FFF0A8'))
                    g.rect(ox + c * 4 + 3, 4 + r * 3, 1, 3, hexc('#E09A10'))
        ox += 16
    g.disc(48, 10, 16, hexc('#FFD54F'), 18)
    vignette(g, 0.25)


SCENES = {
    'end': scene_end, 'jungle': scene_jungle, 'village': scene_village, 'night': scene_night,
    'redstone': scene_redstone, 'plains': scene_plains, 'meadow': lambda g: scene_plains(g, True),
    'mesa': scene_mesa, 'ocean': scene_ocean, 'frost': scene_frost, 'mansion': scene_mansion,
    'color': scene_color, 'bees': scene_bees, 'nether': scene_nether, 'caves': scene_caves,
    'mountain': scene_mountain, 'wild': scene_wild, 'cherry': scene_cherry, 'savanna': scene_savanna,
    'trials': scene_trials, 'pale': scene_pale, 'spring': scene_spring, 'sky': scene_sky,
    'copper': scene_copper, 'mounts': scene_mounts, 'year': scene_year,
}

# (cor do topo ou None, cor do corpo, cor das manchas) do "bloco" de cada tema
BLOCKS = {
    'end': (None, '#E3DE9A', '#C9C27A'), 'jungle': ('#3FA33A', '#6B4A2A', '#7A5A34'),
    'village': (None, '#B8874A', '#8A6030'), 'night': ('#2A4A3A', '#2A2A3A', '#3A3A4C'),
    'redstone': (None, '#7A1510', '#FF3B30'), 'plains': ('#62B84A', '#7A5632', '#8A643A'),
    'meadow': ('#8FD05A', '#7A5632', '#8A643A'), 'mesa': (None, '#C8693C', '#EDE0C4'),
    'ocean': (None, '#3FA79A', '#62C7B5'), 'frost': ('#FFFFFF', '#8FD8F5', '#B6EBFF'),
    'mansion': (None, '#5A3A1E', '#3A2412'), 'color': (None, '#3AB3DA', '#FFFFFF'),
    'bees': (None, '#F2A12A', '#FFD24A'), 'nether': (None, '#7A2A2A', '#9A3B3B'),
    'caves': (None, '#585C68', '#46E8F0'), 'mountain': ('#FFFFFF', '#7E8794', '#6E7784'),
    'wild': (None, '#5A3A22', '#3F8A3F'), 'cherry': ('#F7A6C8', '#7A5632', '#FFC8DE'),
    'savanna': ('#C9B24A', '#8A5A2E', '#9A6A38'), 'trials': (None, '#C8602E', '#3FB59B'),
    'pale': ('#7C8A72', '#D6D1C6', '#B0AA9C'), 'spring': ('#6CD04A', '#7A5632', '#E0A83A'),
    'sky': (None, '#F4F6FA', '#D6DBE6'), 'copper': (None, '#C8602E', '#3FB59B'),
    'mounts': (None, '#E8B66A', '#C48C44'), 'year': (None, '#3B3A8A', '#FFD54F'),
}


def block_icon(key, seed):
    r = random.Random(seed)
    top, body, speck = [hexc(c) if c else None for c in BLOCKS[key]]
    g = Pix(16, 16, seed)
    for y in range(16):
        for x in range(16):
            c = body
            if r.random() < 0.28:
                c = speck
            elif r.random() < 0.2:
                c = shade(body, 0.88)
            g.set(x, y, c)
    if top:
        for x in range(16):
            L = 4 + (1 if r.random() < 0.4 else 0) + (1 if r.random() < 0.2 else 0)
            for y in range(L):
                g.set(x, y, shade(top, 1.0 if r.random() > 0.25 else 0.9))
    # bordas (relevo)
    for i in range(16):
        g.set(i, 0, shade(g.a[0, i, :3].astype(int), 1.15)); g.set(0, i, shade(g.a[i, 0, :3].astype(int), 1.1))
        g.set(i, 15, shade(g.a[15, i, :3].astype(int), 0.7)); g.set(15, i, shade(g.a[i, 15, :3].astype(int), 0.75))
    return g


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else 'versions'
    os.makedirs(out, exist_ok=True)
    for i, (key, fn) in enumerate(SCENES.items()):
        g = Pix(W, H, seed=100 + i)
        fn(g)
        g.save(os.path.join(out, f'{key}.png'))
        block_icon(key, 500 + i).save(os.path.join(out, f'{key}_block.png'))
    print('ok', len(SCENES), 'cenas ->', out)


if __name__ == '__main__':
    main()
