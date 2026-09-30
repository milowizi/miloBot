import math, json, os, sys
import numpy as np
from PIL import Image, ImageDraw

W, H = 48, 48
GROUND = 45
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets")

PAL = {
    "fur":    ["#f2a452", "#dc772c", "#b0541f", "#7e3514"],
    "cream":  ["#fff1d8", "#f5d9ab", "#d8b07a", "#a67c4c"],
    "mask":   ["#9a5530", "#7a3f22", "#5e2e18", "#40200f"],
    "earin":  ["#b5582a", "#94431f", "#763419", "#582511"],
    "nose":   ["#3a2a26", "#1d1311", "#140c0a", "#0a0605"],
    "eye":    ["#2a1a14", "#120b09", "#0b0706", "#060302"],
    "white":  ["#ffffff", "#ffffff", "#e8e8e8", "#c0c0c0"],
    "tongue": ["#f497a6", "#e8718a", "#c4506a", "#9a3450"],
    "mouth":  ["#7a2a2e", "#5a1c20", "#401216", "#2a0a0c"],
}
OUTLINE = {
    "fur": "#4a1e0c", "cream": "#6e4a28", "mask": "#2e160a", "earin": "#3e1a0a",
    "nose": "#0a0605", "eye": "#060302", "white": "#555555", "tongue": "#6a2032", "mouth": "#2a0a0c",
}
KEYS = list(PAL.keys())
K = {k: i for i, k in enumerate(KEYS)}


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def sh(a, dy, dx, fill=0):
    out = np.full_like(a, fill)
    h, w = a.shape[:2]
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    out[yd, xd] = a[ys, xs]
    return out


class Canvas:
    def __init__(self, w=W, h=H):
        self.w, self.h = w, h
        self.key = np.full((h, w), -1, int)
        self.lvl = np.zeros((h, w), int)
        self.anchor = {}

    def paint(self, mask, key, lvl):
        self.key[mask] = K[key]
        self.lvl[mask] = lvl if np.isscalar(lvl) else lvl[mask]

    def filled(self):
        return self.key >= 0

    def ring(self, mask, add=1, cap=3, only=None):
        d = mask.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            d |= sh(mask, dy, dx, False)
        r = d & ~mask & self.filled()
        if only is not None:
            r &= np.isin(self.key, [K[k] for k in only])
        self.lvl[r] = np.minimum(self.lvl[r] + add, cap)

    def strokes(self, pts, add=1, only=("fur",)):
        for x, y in pts:
            x, y = int(round(x)), int(round(y))
            if 0 <= x < self.w and 0 <= y < self.h and self.key[y, x] >= 0 and KEYS[self.key[y, x]] in only:
                self.lvl[y, x] = min(self.lvl[y, x] + add, 3)

    def clip_ground(self, gy=GROUND):
        self.key[gy:, :] = -1

    def blit(self, other, flipv=False, fliph=False):
        k, l = other.key, other.lvl
        if flipv:
            k, l = k[::-1], l[::-1]
        if fliph:
            k, l = k[:, ::-1], l[:, ::-1]
        m = k >= 0
        self.key[m] = k[m]
        self.lvl[m] = l[m]

    def shift(self, dx, dy):
        self.key = sh(self.key, dy, dx, -1)
        self.lvl = sh(self.lvl, dy, dx, 0)

    def image(self, outline=True):
        img = np.zeros((self.h, self.w, 4), np.uint8)
        f = self.filled()
        for i, k in enumerate(KEYS):
            for l in range(4):
                sel = (self.key == i) & (self.lvl == l)
                if sel.any():
                    img[sel, :3] = hexrgb(PAL[k][l])
                    img[sel, 3] = 255
        if outline:
            out = np.zeros_like(f)
            nb = np.full(f.shape, -1)
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                shf = sh(f, dy, dx, False)
                shk = sh(self.key, dy, dx, -1)
                cand = ~f & shf
                nb[cand & (nb < 0)] = shk[cand & (nb < 0)]
                out |= cand
            for i, k in enumerate(KEYS):
                sel = out & (nb == i)
                img[sel, :3] = hexrgb(OUTLINE[k])
                img[sel, 3] = 255
        return Image.fromarray(img, "RGBA")


def grid(c):
    ys, xs = np.mgrid[0:c.h, 0:c.w]
    return xs + 0.5, ys + 0.5


def ellipse_mask(c, cx, cy, rx, ry):
    xs, ys = grid(c)
    return ((xs - cx) / rx) ** 2 + ((ys - cy) / ry) ** 2 <= 1.0


def poly_mask(c, pts):
    im = Image.new("L", (c.w, c.h), 0)
    ImageDraw.Draw(im).polygon([(float(x), float(y)) for x, y in pts], fill=255)
    return np.array(im) > 0


def line_mask(c, pts, width):
    im = Image.new("L", (c.w, c.h), 0)
    ImageDraw.Draw(im).line([(float(x), float(y)) for x, y in pts], fill=255, width=width)
    return np.array(im) > 0


def rect_mask(c, x0, y0, x1, y1):
    m = np.zeros((c.h, c.w), bool)
    x0, y0, x1, y1 = int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1))
    m[max(0, y0):max(0, y1), max(0, x0):max(0, x1)] = True
    return m


def px(c, x, y, key, lvl):
    x, y = int(round(x)), int(round(y))
    if 0 <= x < c.w and 0 <= y < c.h:
        c.key[y, x] = K[key]
        c.lvl[y, x] = lvl


def shade_ellipse(c, cx, cy, rx, ry, top=0.45, bottom=0.35):
    xs, ys = grid(c)
    d = -(ys - cy) / ry * 0.95
    lvl = np.ones((c.h, c.w), int)
    lvl[d > top] = 0
    lvl[d < -bottom] = 2
    lvl[d < -0.85] = 3
    return lvl


def cloud_mask(c, cx, cy, rx, ry, bumps, br=2.2, rot=0):
    m = ellipse_mask(c, cx, cy, rx, ry)
    for a, s in bumps:
        a = math.radians(a + rot)
        m |= ellipse_mask(c, cx + rx * math.cos(a), cy + ry * math.sin(a), br * s, br * s)
    return m


def cloud(c, key, cx, cy, rx, ry, bumps, br=2.2, top=0.45, bottom=0.35, rot=0, lvl_add=0):
    m = cloud_mask(c, cx, cy, rx, ry, bumps, br, rot)
    lvl = np.clip(shade_ellipse(c, cx, cy, rx + br, ry + br, top, bottom) + lvl_add, 0, 3)
    c.paint(m, key, lvl)
    return m


BODY_B = [(-160, 1.0), (-130, 1.0), (-100, 0.9), (-70, 0.9), (180, 1.0), (150, 1.0), (120, 0.9), (60, 0.8)]
MANE_B = [(-150, 1.1), (-120, 1.0), (180, 1.1), (150, 1.1), (120, 1.0), (-175, 1.0)]
RUFF_B = [(30, 0.9), (60, 1.0), (90, 1.0), (120, 0.9), (0, 0.8)]
TAIL_B = [(-170, 1.0), (-140, 1.1), (-110, 1.1), (-80, 1.0), (-50, 1.0), (-20, 0.9), (170, 0.9), (10, 0.8)]
ROUND_B = [(-150, 1.0), (-110, 1.0), (-70, 1.0), (-30, 0.9), (10, 0.9), (180, 1.0), (140, 1.0), (40, 0.9), (90, 0.8)]


# ---------------------------------------------------------------- side view parts

def leg(c, x, top, fx, fy, near, key="cream", width=3, paw=True):
    lvl = 1 if near else 2
    m = line_mask(c, [(x + 1, top), (fx + 1, fy - 2)], width)
    if paw:
        m |= rect_mask(c, fx, fy - 2, fx + 3, fy)
    c.paint(m, key, lvl)
    if near and paw:
        c.paint(rect_mask(c, fx + 1, fy - 1, fx + 2, fy), key, 2)


def stand_legs(phase=None, lift=0.0, stride=0.0, gy=GROUND, top=39, dx=0):
    spec = [(17, False, 0.5), (29, False, 0.0), (14, True, 0.0), (26, True, 0.5)]
    legs = []
    for hx, near, off in spec:
        hx += dx
        if phase is None:
            fx, fy = hx, gy
        else:
            ph = (phase + off) % 1.0
            fx = hx + math.sin(ph * 2 * math.pi) * stride
            fy = gy - max(0.0, math.cos(ph * 2 * math.pi)) * lift
        legs.append((hx, top, fx, fy, near))
    return legs


def tail_up(c, bx, by, brx, tx, ty, sway=0, rot=0):
    tx += sway
    c.paint(line_mask(c, [(bx - brx + 2, by - 1), (tx + 2, ty + 2)], 5), "fur", 1)
    m = cloud_mask(c, tx, ty, 5.5, 3.6, TAIL_B, 2.0, rot)
    c.ring(m, 1, only=("fur",))
    c.paint(m, "fur", np.clip(shade_ellipse(c, tx, ty, 7.5, 5.6, -0.05, 0.6) - 1, 0, 3))
    tip = cloud_mask(c, tx - 3.5, ty - 2.0, 2.2, 1.4, [(-150, 0.8), (-110, 0.7), (180, 0.8)], 1.4)
    c.paint(tip & m, "cream", 1)
    c.strokes([(tx + 2, ty + 1), (tx + 3, ty + 1), (tx - 2, ty + 1), (tx - 1, ty + 2)])


def tail_down(c, x0, y0, x1, y1):
    c.paint(line_mask(c, [(x0, y0), (x1, y1)], 4), "fur", 2)
    c.paint(cloud_mask(c, x1, y1 + 1, 2.2, 2.2, [(90, 1.0), (160, 0.9), (20, 0.9)], 1.5), "fur", 2)


def body_side(c, bx, by, rx, ry, rot=0):
    m = cloud(c, "fur", bx, by, rx, ry, BODY_B, 2.3, rot=rot)
    c.paint(ellipse_mask(c, bx + 1, by + ry, rx * 0.75, 2.0) & m, "cream", 2)
    c.strokes([(bx - 6, by - 2), (bx - 5, by - 1), (bx + 2, by - 3), (bx + 3, by - 2), (bx - 2, by + 1), (bx - 1, by + 2)])
    return m


def ears_side(c, hx, hy, e):
    if e == "up":
        far = [(hx - 4.8, hy - 3.5), (hx - 1.0, hy - 4.8), (hx - 4.2, hy - 10.5)]
        near = [(hx - 2.4, hy - 4.0), (hx + 2.6, hy - 4.6), (hx - 0.6, hy - 11.0)]
    elif e == "perk":
        far = [(hx - 4.2, hy - 3.5), (hx - 0.5, hy - 4.8), (hx - 2.8, hy - 11.2)]
        near = [(hx - 1.8, hy - 4.0), (hx + 3.0, hy - 4.6), (hx + 0.9, hy - 11.8)]
    elif e == "twitch":
        far = [(hx - 4.8, hy - 3.5), (hx - 1.0, hy - 4.8), (hx - 4.2, hy - 10.5)]
        near = [(hx - 2.4, hy - 4.0), (hx + 2.6, hy - 4.6), (hx - 2.6, hy - 10.2)]
    else:
        far = [(hx - 3.5, hy - 3.2), (hx - 0.5, hy - 5.0), (hx - 8.0, hy - 6.2)]
        near = [(hx - 1.5, hy - 3.8), (hx + 1.5, hy - 5.4), (hx - 6.5, hy - 7.4)]
    c.paint(poly_mask(c, far), "fur", 2)
    nm = poly_mask(c, near)
    c.ring(nm, 2, only=("fur",))
    c.paint(nm, "fur", 1)
    cx = (near[0][0] + near[1][0]) / 2
    cy = (near[0][1] + near[1][1]) / 2
    inner = [(near[0][0] * 0.6 + cx * 0.4, near[0][1] * 0.6 + cy * 0.4 + 0.3),
             (near[1][0] * 0.6 + cx * 0.4, near[1][1] * 0.6 + cy * 0.4 + 0.3),
             (near[2][0] * 0.65 + cx * 0.35, near[2][1] * 0.65 + cy * 0.35)]
    c.paint(poly_mask(c, inner) & nm, "earin", 1)


def head_side(c, hx, hy, eye="open", mouth="closed", ears="up", mane=True, ruff=True, rot=0, snout="side"):
    if mane:
        cloud(c, "fur", hx - 2, hy + 2.5, 7.4, 6.8, MANE_B, 2.4, rot=rot)
        c.strokes([(hx - 8, hy + 1), (hx - 7, hy + 2), (hx - 5, hy + 6), (hx - 4, hy + 7)])
    if ruff:
        r = cloud(c, "cream", hx + 0.5, hy + 7.0, 4.6, 4.2, RUFF_B, 2.0, rot=rot)
        c.lvl[r] = np.maximum(c.lvl[r], 1)
    ears_side(c, hx, hy, ears)
    head = ellipse_mask(c, hx + 0.5, hy, 6.3, 5.8)
    c.ring(head, 1, only=("fur", "cream"))
    c.paint(head, "fur", shade_ellipse(c, hx, hy - 0.5, 6.8, 6.2, 0.4, 0.6))
    c.paint(ellipse_mask(c, hx + 2.5, hy + 3.6, 3.2, 1.8) & head, "cream", 1)
    c.anchor["hat"] = (hx - 0.3, hy - 4.6, "side")
    if snout == "up":
        mz = ellipse_mask(c, hx + 5.2, hy - 2.2, 2.4, 3.0)
        c.paint(mz, "mask", 1)
        c.paint(ellipse_mask(c, hx + 4.6, hy - 1.0, 1.6, 1.6) & mz, "mask", 0)
        c.paint(rect_mask(c, hx + 5, hy - 6, hx + 7, hy - 4), "nose", 1)
        px(c, hx + 5, hy - 6, "nose", 0)
        c.paint(rect_mask(c, hx + 6, hy - 3, hx + 8, hy), "mouth", 1)
        face_side(c, hx, hy, eye, "none")
        c.anchor["mouth"] = (hx + 6.5, hy - 2)
        c.anchor["head"] = (hx + 2, hy - 11)
        return
    mz = ellipse_mask(c, hx + 6.9, hy + 1.8, 3.0, 2.0)
    c.paint(mz, "mask", np.clip(shade_ellipse(c, hx + 6.9, hy + 1.8, 3.0, 2.0, 0.3, 0.5) - 1, 0, 3))
    c.paint(ellipse_mask(c, hx + 4.8, hy + 0.6, 2.0, 1.4) & ~mz, "mask", 0)
    nx, ny = int(round(hx + 9.0)), int(round(hy + 0.6))
    c.paint(rect_mask(c, nx - 1, ny, nx + 1, ny + 2), "nose", 1)
    px(c, nx - 1, ny, "nose", 0)
    face_side(c, hx, hy, eye, mouth)
    c.anchor["mouth"] = (hx + 7.5, hy + 3.5)
    c.anchor["head"] = (hx, hy - 9)


def face_side(c, hx, hy, e, m):
    ex, ey = int(round(hx + 2.6)), int(round(hy - 2.0))
    c.anchor["eye"] = (ex + 0.5, ey + 1, "side")
    if e in ("open", "wide"):
        h = 3 if e == "open" else 4
        c.paint(rect_mask(c, ex, ey - (1 if e == "wide" else 0), ex + 2, ey + 3), "eye", 1)
        px(c, ex, ey - (1 if e == "wide" else 0), "white", 0)
    elif e == "half":
        c.paint(rect_mask(c, ex, ey + 1, ex + 2, ey + 3), "eye", 1)
        c.paint(rect_mask(c, ex - 1, ey + 1, ex + 3, ey + 2), "mask", 2)
    elif e == "closed":
        c.paint(rect_mask(c, ex - 1, ey + 2, ex + 2, ey + 3), "eye", 2)
    elif e == "happy":
        px(c, ex - 1, ey + 2, "eye", 1)
        c.paint(rect_mask(c, ex, ey + 1, ex + 2, ey + 2), "eye", 1)
        px(c, ex + 2, ey + 2, "eye", 1)
    elif e == "up":
        c.paint(rect_mask(c, ex, ey - 1, ex + 2, ey + 2), "eye", 1)
        px(c, ex + 1, ey - 1, "white", 0)
    elif e == "x":
        for dx, dy in ((0, 0), (2, 0), (1, 1), (0, 2), (2, 2)):
            px(c, ex - 1 + dx, ey + dy, "eye", 1)
    elif e == "down":
        c.paint(rect_mask(c, ex, ey + 1, ex + 2, ey + 4), "eye", 1)
        c.paint(rect_mask(c, ex - 1, ey + 1, ex + 3, ey + 2), "mask", 2)
        px(c, ex, ey + 2, "white", 0)
    elif e == "dizzy":
        for dx, dy in ((0, 0), (1, 0), (2, 1), (1, 2), (0, 2), (-1, 1)):
            px(c, ex + dx, ey + dy, "eye", 1)
        px(c, ex + 1, ey + 1, "eye", 1)
    mx, my = int(round(hx + 5)), int(round(hy + 3))
    if m == "open":
        c.paint(poly_mask(c, [(mx, my), (mx + 5, my), (mx + 4, my + 3), (mx + 1, my + 2.5)]), "mouth", 1)
        c.paint(rect_mask(c, mx + 2, my + 2, mx + 4, my + 3), "tongue", 1)
        c.paint(rect_mask(c, mx + 1, my + 3, mx + 5, my + 4), "mask", 1)
    elif m == "pant":
        c.paint(rect_mask(c, mx, my, mx + 4, my + 1), "mouth", 1)
        c.paint(rect_mask(c, mx + 1, my + 1, mx + 4, my + 4), "tongue", 1)
        c.paint(rect_mask(c, mx + 2, my + 1, mx + 3, my + 3), "tongue", 2)
        c.paint(rect_mask(c, mx + 1, my + 3, mx + 4, my + 4), "tongue", 2)
    elif m == "yawn":
        c.paint(poly_mask(c, [(mx - 1, my - 0.5), (mx + 5, my - 1), (mx + 4.5, my + 4), (mx, my + 3.5)]), "mouth", 1)
        c.paint(rect_mask(c, mx + 1, my + 2, mx + 4, my + 4), "tongue", 1)
        c.paint(rect_mask(c, mx, my + 4, mx + 5, my + 5), "mask", 2)
    elif m == "yawn_small":
        c.paint(rect_mask(c, mx, my, mx + 4, my + 2), "mouth", 1)
        c.paint(rect_mask(c, mx + 1, my + 2, mx + 5, my + 3), "mask", 2)
    elif m == "tongue":
        c.paint(rect_mask(c, mx, my, mx + 4, my + 1), "mouth", 1)
        c.paint(rect_mask(c, mx + 2, my + 1, mx + 4, my + 5), "tongue", 1)
        px(c, mx + 3, my + 4, "tongue", 2)
    elif m == "hold":
        c.paint(rect_mask(c, mx + 1, my, mx + 5, my + 2), "mouth", 1)


# ---------------------------------------------------------------- side view poses

def stand(dy=0, eye="open", mouth="closed", ears="up", sway=0, legs=None, hx=31, hy=26, bx=20, by=36,
          brx=10.5, bry=6.2, rot=0, tail="up", head_rot=0):
    c = Canvas()
    legs = legs or stand_legs()
    for L in legs:
        if not L[4]:
            leg(c, L[0], L[1] + dy, L[2], L[3], False)
    if tail == "down":
        tail_down(c, bx - brx + 1, by - 1 + dy, bx - brx - 1, by + 6 + dy)
    body_side(c, bx, by + dy, brx, bry, rot)
    for L in legs:
        if L[4]:
            leg(c, L[0], L[1] + dy, L[2], L[3], True)
    if tail == "up":
        tail_up(c, bx, by + dy, brx, 12 + (bx - 20), 28.5 + dy + (by - 36), sway, rot)
    head_side(c, hx, hy + dy, eye, mouth, ears, rot=head_rot)
    c.clip_ground()
    return c


def sit(dy=0, eye="open", mouth="closed", ears="up", hx=30, hy=23, sway=0, scratch=None, head_rot=0):
    c = Canvas()
    leg(c, 29, 35 + dy, 29, GROUND, False)
    cloud(c, "fur", 17, 39 + dy * 0.5, 7.5, 5.2, BODY_B, 2.2)
    tail_up(c, 21, 40 + dy, 8, 10 + sway, 32 + dy, 0)
    cloud(c, "fur", 24.5, 33 + dy, 5.5, 6.2, ROUND_B, 2.0)
    thigh = ellipse_mask(c, 17.5, 39.5 + dy * 0.5, 4.5, 3.8)
    c.ring(thigh, 1, only=("fur",))
    c.paint(thigh, "fur", 1)
    c.paint(rect_mask(c, 19, 43, 24, GROUND), "cream", 1)
    px(c, 22, 44, "cream", 2)
    if scratch is not None:
        fx, fy = scratch
        c.paint(line_mask(c, [(19, 38), (fx, fy)], 3), "fur", 1)
        c.paint(rect_mask(c, fx - 1, fy - 1, fx + 2, fy + 2), "cream", 1)
    leg(c, 26, 35 + dy, 26, GROUND, True)
    head_side(c, hx, hy + dy, eye, mouth, ears, rot=head_rot)
    c.clip_ground()
    return c


def lie(dy=0, eye="open", mouth="closed", ears="up", hx=31, hy=31, sway=0, head_rot=0):
    c = Canvas()
    leg(c, 30, 41, 36, GROUND, False)
    body_side(c, 20, 39.5 + dy * 0.5, 11, 4.8)
    tail_up(c, 20, 40 + dy, 11, 10 + sway, 34 + dy, 0)
    leg(c, 27, 41, 34, GROUND, True)
    head_side(c, hx, hy + dy, eye, mouth, ears, rot=head_rot)
    c.clip_ground()
    return c


def sleep(breath=0, ears="back"):
    c = Canvas()
    m = cloud(c, "fur", 21, 39.5 - breath * 0.4, 12, 5.6 + breath * 0.4, BODY_B, 2.3)
    c.paint(ellipse_mask(c, 22, 44, 9, 2) & m, "cream", 2)
    c.strokes([(15, 37), (16, 38), (24, 36), (25, 37)])
    tail_up(c, 21, 40, 12, 10, 35 - breath * 0.5, 0)
    c.paint(rect_mask(c, 35, 43, 40, GROUND), "cream", 1)
    head_side(c, 31, 36.5, "closed", "closed", ears, mane=True, ruff=False)
    c.clip_ground()
    return c


def sniff(dy=0, twitch=0, legs=None, eye="half", mouth="closed"):
    c = Canvas()
    legs = legs or stand_legs()
    for L in legs:
        if not L[4]:
            leg(c, L[0], L[1] + dy, L[2], L[3], False)
    body_side(c, 20, 36 + dy, 10.5, 6.2)
    for L in legs:
        if L[4]:
            leg(c, L[0], L[1] + dy, L[2], L[3], True)
    tail_up(c, 20, 36 + dy, 10.5, 12, 28.5 + dy, 0)
    head_side(c, 32 + twitch, 32 + dy, eye, mouth, "up")
    c.clip_ground()
    return c


def bow(sway=0, eye="open", mouth="pant"):
    c = Canvas()
    leg(c, 17, 33, 17, GROUND, False, "fur")
    leg(c, 30, 41, 37, GROUND, False)
    cloud(c, "fur", 15, 32, 7, 5.2, BODY_B, 2.2)
    cloud(c, "fur", 25, 38, 6.5, 4.5, ROUND_B, 2.0)
    tail_up(c, 16, 31, 5, 12 + sway, 22, 0)
    leg(c, 14, 33, 14, GROUND, True, "fur")
    c.paint(rect_mask(c, 14, 43, 17, GROUND), "cream", 1)
    leg(c, 27, 41, 35, GROUND, True)
    head_side(c, 33, 33, eye, mouth, "perk")
    c.clip_ground()
    return c


def held(swing=0, eye="wide", mouth="closed", ears="back"):
    c = Canvas()
    leg(c, 29, 35, 29 + swing, 43, False)
    leg(c, 22, 35, 22 + swing, 44, False, "fur")
    m = cloud(c, "fur", 24, 30, 6.5, 8.0, ROUND_B, 2.1)
    c.paint(ellipse_mask(c, 26, 32, 3.5, 5) & m, "cream", 1)
    tail_down(c, 19, 34, 18 + swing, 41)
    leg(c, 20, 35, 20 + swing, 45, True, "fur")
    leg(c, 27, 35, 27 + swing, 44, True)
    head_side(c, 25, 17, eye, mouth, ears)
    return c


def fall(eye="wide", mouth="open", flap=0):
    legs = [(14, 38, 8, 43 - flap, False), (30, 38, 36, 42 - flap, False),
            (15, 38, 9, 44 + flap, True), (27, 38, 33, 44 + flap, True)]
    c = Canvas()
    for L in legs:
        if not L[4]:
            leg(c, L[0], L[1], L[2], L[3], False)
    body_side(c, 20, 35, 10.5, 6.0)
    for L in legs:
        if L[4]:
            leg(c, L[0], L[1], L[2], L[3], True)
    tail_up(c, 20, 35, 10.5, 12, 26 - flap, 0)
    head_side(c, 31, 24, eye, mouth, "perk" if flap else "up")
    return c


def beg(bob=0, mouth="pant", eye="open"):
    c = Canvas()
    tail_down(c, 15, 41, 9, 43)
    cloud(c, "fur", 19, 41, 7, 3.6, BODY_B, 2.0)
    m = cloud(c, "fur", 21, 33, 5.8, 7.5, ROUND_B, 2.0)
    c.paint(ellipse_mask(c, 24, 34, 3, 5) & m, "cream", 1)
    c.paint(rect_mask(c, 22, 43, 27, GROUND), "cream", 1)
    px(c, 25, 44, "cream", 2)
    head_side(c, 24, 19 + bob * 0.5, eye, mouth, "up")
    c.paint(line_mask(c, [(24, 29 + bob), (28, 30 + bob)], 3), "cream", 2)
    c.paint(line_mask(c, [(25, 30 + bob), (29, 31 + bob)], 3), "cream", 1)
    c.paint(rect_mask(c, 28, 29 + bob, 31, 32 + bob), "cream", 1)
    c.clip_ground()
    return c


def belly(wiggle=0):
    c = Canvas()
    leg(c, 16, 37, 14 + wiggle, 27, False, "fur")
    leg(c, 27, 37, 28 - wiggle, 28, False)
    m = cloud(c, "fur", 20, 40, 11, 4.6, BODY_B, 2.2)
    c.paint(ellipse_mask(c, 21, 37, 8, 2.6) & m, "cream", 0)
    leg(c, 19, 37, 18 - wiggle, 28, True, "fur")
    leg(c, 29, 37, 31 + wiggle, 29, True)
    h = Canvas()
    head_side(h, 32, 12, "happy", "pant", "back", ruff=False)
    f = Canvas()
    f.blit(h, flipv=True)
    f.shift(2, 2)
    c.blit(f)
    c.clip_ground()
    c.anchor["mouth"] = (40, 36)
    c.anchor["head"] = (30, 24)
    return c


def side_lie(phase=None, eye="closed", mouth="closed", dy=0):
    c = Canvas()
    tail_up(c, 20, 41, 11, 9, 38, 0)
    body = cloud(c, "fur", 20, 40.5 + dy * 0.3, 11, 4.2, BODY_B, 2.0)
    c.paint(ellipse_mask(c, 22, 42.5, 8, 1.8) & body, "cream", 1)
    spec = [(15, 0.0), (18, 0.5), (25, 0.25), (28, 0.75)]
    for i, (x, off) in enumerate(spec):
        k = 0 if phase is None else math.sin((phase + off) * 2 * math.pi) * 2.5
        near = i in (0, 2)
        leg(c, x, 41, x + 6 + k, 44.5, near, "cream" if i >= 2 else "fur", 3, True)
    head_side(c, 32, 39 + dy, eye, mouth, "back", ruff=False)
    c.clip_ground()
    return c


def dead(twitch=0):
    c = Canvas()
    for x, near, key in ((16, False, "fur"), (27, False, "cream"), (19, True, "fur"), (29, True, "cream")):
        leg(c, x, 37, x + (twitch if near else 0), 27, near, key)
    m = cloud(c, "fur", 20, 40, 11, 4.6, BODY_B, 2.2)
    c.paint(ellipse_mask(c, 21, 37, 8, 2.6) & m, "cream", 0)
    h = Canvas()
    head_side(h, 32, 12, "x", "tongue", "back", ruff=False)
    f = Canvas()
    f.blit(h, flipv=True)
    f.shift(2, 2)
    c.blit(f)
    c.clip_ground()
    c.anchor["mouth"] = (40, 36)
    c.anchor["head"] = (30, 24)
    return c


def back_view(dy=0, sway=0):
    c = Canvas()
    cx = 24
    for s_ in (-1, 1):
        cloud(c, "fur", cx + s_ * 8, 41 + dy * 0.5, 4.5, 3.4, ROUND_B, 1.6, lvl_add=1)
        c.paint(rect_mask(c, cx + s_ * 9 - 2, 43, cx + s_ * 9 + 2, GROUND), "cream", 2)
    cloud(c, "fur", cx, 36 + dy, 8.5, 7.5, ROUND_B, 2.0)
    hy = 24 + dy
    for s_ in (-1, 1):
        e = [(cx + s_ * 2.5, hy - 4.5), (cx + s_ * 7.2, hy - 2.5), (cx + s_ * 7.0, hy - 11.0)]
        em = poly_mask(c, e)
        c.ring(em, 2, only=("fur",))
        c.paint(em, "fur", 2)
    head = cloud(c, "fur", cx, hy + 1.5, 7.8, 6.8, [(a, 1.0) for a in range(-180, 180, 30)], 2.0)
    c.strokes([(cx - 3, hy - 1), (cx - 2, hy), (cx + 2, hy + 1), (cx + 3, hy + 2), (cx - 1, hy + 4)])
    tail = cloud(c, "fur", cx + sway, 33 + dy, 5.5, 4.5, TAIL_B, 2.0)
    c.ring(tail, 1, only=("fur",))
    c.paint(tail, "fur", np.clip(shade_ellipse(c, cx + sway, 33 + dy, 7, 6, 0.1, 0.5) - 1, 0, 3))
    c.strokes([(cx + sway - 2, 33 + dy), (cx + sway + 2, 34 + dy), (cx + sway, 35 + dy)])
    c.clip_ground()
    c.anchor["mouth"] = (cx, hy + 6)
    c.anchor["head"] = (cx, hy - 12)
    c.anchor["hat"] = (cx, hy - 4.8, "front")
    return c


def tug(shake=0, sway=0):
    c = Canvas()
    legs = [(15, 38, 11, GROUND, False), (27, 39, 32, GROUND, False), (13, 38, 9, GROUND, True), (25, 39, 30, GROUND, True)]
    for L in legs:
        if not L[4]:
            leg(c, L[0], L[1], L[2], L[3], False)
    body_side(c, 18, 37, 10, 5.8)
    for L in legs:
        if L[4]:
            leg(c, L[0], L[1], L[2], L[3], True)
    tail_up(c, 18, 37, 10, 11 + sway, 29, 0)
    head_side(c, 30, 31 + shake, "happy", "hold", "back")
    c.clip_ground()
    return c


def howl(breath=0):
    c = Canvas()
    leg(c, 29, 35, 29, GROUND, False)
    cloud(c, "fur", 17, 39, 7.5, 5.2, BODY_B, 2.2)
    tail_up(c, 21, 40, 8, 10, 32, 0)
    cloud(c, "fur", 24.5, 32.5, 5.5, 6.6, ROUND_B, 2.0)
    thigh = ellipse_mask(c, 17.5, 39.5, 4.5, 3.8)
    c.ring(thigh, 1, only=("fur",))
    c.paint(thigh, "fur", 1)
    c.paint(rect_mask(c, 19, 43, 24, GROUND), "cream", 1)
    leg(c, 26, 35, 26, GROUND, True)
    head_side(c, 28, 20 - breath, "closed", "none", "back", snout="up")
    c.clip_ground()
    return c


def flipped(c):
    f = Canvas()
    f.blit(c, fliph=True)
    for k, v in c.anchor.items():
        if k in ("hat", "eye"):
            f.anchor[k] = (W - v[0], v[1], v[2])
        elif k != "_flipped":
            f.anchor[k] = (W - v[0], v[1])
    f.anchor["_flipped"] = not c.anchor.get("_flipped", False)
    return f


def peek(paw=None, eye="down", ears="perk", dy=0):
    c = Canvas()
    leg(c, 30, 41, 38, GROUND, False)
    body_side(c, 19, 40 + dy * 0.5, 11, 4.6)
    tail_up(c, 19, 40 + dy, 11, 9, 34 + dy, 0)
    if paw is None:
        leg(c, 28, 41, 36, GROUND, True)
    head_side(c, 33, 35 + dy, eye, "closed", ears)
    c.clip_ground()
    if paw is not None:
        fx, fy = paw
        c.paint(line_mask(c, [(29, 41), (fx, fy - 1)], 3), "cream", 1)
        pm = ellipse_mask(c, fx + 0.5, fy, 2.0, 1.6)
        c.paint(pm, "cream", 0)
        px(c, fx, fy + 1, "cream", 2)
    return c


def surf(rot=0, sway=0):
    legs = [(17, 38, 10, GROUND, False), (29, 38, 36, GROUND, False), (14, 38, 7, GROUND, True), (26, 38, 33, GROUND, True)]
    return stand(dy=2, legs=legs, ears="back", eye="wide", mouth="pant", rot=rot, head_rot=rot, sway=sway, hy=27)


def dizzy(sway=0):
    return sit(eye="dizzy", mouth="tongue", ears="back", hx=30 + sway, hy=23 + abs(sway) * 0.5, head_rot=sway * 12)


# ---------------------------------------------------------------- front view

def front(eye="open", mouth="closed", tilt=0, dy=0, ears="up", look=0, paw=0):
    c = Canvas()
    cx = 24
    for s in (-1, 1):
        cloud(c, "fur", cx + s * 8.5, 40.5 + dy * 0.5, 4.5, 3.5, ROUND_B, 1.6, lvl_add=1)
        c.paint(rect_mask(c, cx + s * 9.5 - 2, 43, cx + s * 9.5 + 2, GROUND), "cream", 2)
    cloud(c, "fur", cx, 36.5 + dy, 8.0, 7.0, ROUND_B, 2.0)
    ch = cloud(c, "cream", cx, 35 + dy, 4.8, 6.5, [(60, 1.0), (90, 1.1), (120, 1.0), (30, 0.8), (150, 0.8)], 1.8)
    c.lvl[ch] = np.maximum(c.lvl[ch], 1)
    for s in (-1, 1):
        lx = cx + s * 3.5
        if paw and s > 0:
            continue
        c.paint(rect_mask(c, lx - 1.5, 38, lx + 1.5, GROUND), "cream", 1)
        c.paint(rect_mask(c, lx - 1.5, 38, lx + 1.5, 39), "cream", 2)
        px(c, lx - 0.5 if s < 0 else lx - 0.5, 44, "cream", 2)
    hy = 24 + dy
    cloud(c, "fur", cx, hy + 3.5, 9.2, 7.2, [(a, 1.0) for a in range(-180, 180, 30)], 2.2)
    for s in (-1, 1):
        t = tilt * s
        if ears == "back":
            e = [(cx + s * 3.5, hy - 4.5), (cx + s * 7.5, hy - 2.0), (cx + s * 10.5, hy - 6.5 + t)]
        else:
            e = [(cx + s * 2.5, hy - 5.0), (cx + s * 7.2, hy - 3.0), (cx + s * 7.0, hy - 11.5 + t)]
        em = poly_mask(c, e)
        c.ring(em, 2, only=("fur",))
        c.paint(em, "fur", 1)
        inner = [(e[0][0] * 0.55 + e[1][0] * 0.45, e[0][1] * 0.5 + e[1][1] * 0.5 + 0.5),
                 (e[1][0] * 0.7 + e[0][0] * 0.3, e[1][1] * 0.7 + e[0][1] * 0.3),
                 (e[2][0] * 0.7 + e[0][0] * 0.15 + e[1][0] * 0.15, e[2][1] * 0.7 + (e[0][1] + e[1][1]) * 0.15 + 0.8)]
        c.paint(poly_mask(c, inner) & em, "earin", 1)
    head = ellipse_mask(c, cx, hy, 7.2, 6.2)
    c.anchor["hat"] = (cx, hy - 5.2 + (0 if not tilt else 0), "front")
    c.anchor["eye"] = (cx, hy - 1 - look, "front")
    c.ring(head, 1, only=("fur",))
    c.paint(head, "fur", shade_ellipse(c, cx, hy - 0.5, 7.5, 6.5, 0.45, 0.6))
    for s in (-1, 1):
        c.paint(ellipse_mask(c, cx + s * 4.2, hy + 3.4, 2.8, 1.8), "cream", 1)
    c.paint(ellipse_mask(c, cx, hy + 3.4, 3.0, 2.2), "mask", 0)
    c.paint(ellipse_mask(c, cx, hy + 1.2, 1.5, 2.0), "mask", 0)
    c.paint(rect_mask(c, cx - 1, hy + 1, cx + 1, hy + 3), "nose", 1)
    px(c, cx - 1, hy + 1, "nose", 0)
    for s in (-1, 1):
        ex = cx - 5 if s < 0 else cx + 3
        ey = hy - 2 + (tilt * s if tilt else 0) - look
        if eye == "open":
            c.paint(rect_mask(c, ex, ey, ex + 2, ey + 3), "eye", 1)
            px(c, ex, ey, "white", 0)
        elif eye == "closed":
            c.paint(rect_mask(c, ex, ey + 2, ex + 2, ey + 3), "eye", 2)
            px(c, ex - (1 if s < 0 else -2), ey + 2, "eye", 2)
        elif eye == "happy":
            px(c, ex - 1 if s < 0 else ex - 1, ey + 2, "eye", 1)
            c.paint(rect_mask(c, ex, ey + 1, ex + 2, ey + 2), "eye", 1)
            px(c, ex + 2, ey + 2, "eye", 1)
        elif eye == "wink" :
            if s < 0:
                c.paint(rect_mask(c, ex, ey, ex + 2, ey + 3), "eye", 1)
                px(c, ex, ey, "white", 0)
            else:
                c.paint(rect_mask(c, ex - 1, ey + 2, ex + 2, ey + 3), "eye", 2)
    my = hy + 4
    if mouth == "closed":
        px(c, cx - 2, my, "mouth", 1)
        c.paint(rect_mask(c, cx - 1, my + 1, cx + 1, my + 2), "mouth", 1)
        px(c, cx + 1, my, "mouth", 1)
    elif mouth == "pant":
        c.paint(rect_mask(c, cx - 2, my, cx + 2, my + 1), "mouth", 1)
        c.paint(rect_mask(c, cx - 1, my + 1, cx + 1, my + 4), "tongue", 1)
        px(c, cx, my + 1, "tongue", 2)
        c.paint(rect_mask(c, cx - 1, my + 3, cx + 1, my + 4), "tongue", 2)
    elif mouth == "open":
        c.paint(rect_mask(c, cx - 2, my, cx + 2, my + 3), "mouth", 1)
        c.paint(rect_mask(c, cx - 1, my + 2, cx + 1, my + 3), "tongue", 1)
    elif mouth == "hold":
        c.paint(rect_mask(c, cx - 2, my, cx + 2, my + 1), "mouth", 1)
    if paw:
        lx, py = cx + 3.5, 32 - paw
        c.paint(line_mask(c, [(lx, 38), (lx + 3, py + 1)], 3), "cream", 1)
        pm = ellipse_mask(c, lx + 3.5, py, 2.6, 2.1)
        c.ring(pm, 2, only=("fur", "cream"))
        c.paint(pm, "cream", 0)
        px(c, lx + 3, py + 1, "cream", 2)
        px(c, lx + 5, py + 1, "cream", 2)
    c.clip_ground()
    c.anchor["mouth"] = (cx, hy + 5)
    c.anchor["head"] = (cx, hy - 12)
    return c


# ---------------------------------------------------------------- animations

def walk_frames(n=6, stride=2.2, lift=1.6, eye="open", mouth="closed", head_down=False, run=False, ears="up", stretch=0.8):
    out = []
    for i in range(n):
        ph = i / n
        bob = 1 if (i % (n // 2)) in (1, 2) and not run else 0
        if run:
            bob = [0, -1, 0, 1][i % 4]
        legs = stand_legs(ph, lift, stride)
        sway = [0, 1, 1, 0, -1, -1][i % 6] if not run else [1, 0, -1, 0][i % 4]
        if head_down:
            out.append(sniff(dy=bob, twitch=(i % 2) * 0.5, legs=legs, eye="half"))
        else:
            out.append(stand(dy=bob, eye=eye, mouth=mouth, ears=ears, sway=sway, legs=legs,
                             brx=10.5 + (stretch if run and i % 2 == 0 else 0)))
    return out


def build():
    A = {}

    def add(name, frames, fps, loop=True):
        A[name] = dict(frames=frames, fps=fps, loop=loop)

    add("idle", [stand(), stand(dy=1)], 2.2)
    add("idle_blink", [stand(eye="closed")], 6, False)
    add("idle_ear", [stand(ears="twitch"), stand(), stand(ears="twitch"), stand()], 10, False)
    add("idle_wag", [stand(sway=s) for s in (0, 1, 2, 1, 0, -1)], 10)
    add("idle_look", [stand(ears="perk", eye="up")], 2)
    add("alert", [stand(ears="perk", eye="wide")], 2)
    add("happy", [stand(eye="happy", mouth="pant", sway=s, dy=d) for s, d in ((-2, 0), (0, 1), (2, 0), (0, 1))], 12)
    add("walk", walk_frames(6), 10)
    add("walk_happy", walk_frames(6, mouth="pant"), 11)
    add("run", walk_frames(4, stride=3.2, lift=2.6, mouth="pant", run=True), 14)
    add("sniff", [sniff(twitch=0), sniff(twitch=0.6), sniff(twitch=0), sniff(twitch=0.6, eye="closed")], 6)
    add("sniff_walk", walk_frames(6, stride=1.6, lift=1.0, head_down=True), 7)
    add("bark", [stand(ears="perk", mouth="open", dy=-1), stand(ears="perk"), stand(ears="perk", mouth="open", dy=-1), stand(ears="perk")], 8, False)
    add("growl", [stand(ears="back", mouth="open", tail="down"), stand(ears="back", tail="down")], 4)
    add("scared", [stand(ears="back", eye="wide", tail="down", dy=1), stand(ears="back", eye="wide", tail="down", dy=2)], 8)
    add("sit_down", [stand(dy=1, hy=25), sit()], 8, False)
    add("sit", [sit(), sit(dy=1)], 2)
    add("sit_blink", [sit(eye="closed")], 6, False)
    add("sit_look", [sit(eye="up", ears="perk")], 2)
    add("sit_pant", [sit(mouth="pant", eye="happy"), sit(mouth="pant", eye="happy", dy=1)], 4)
    add("stand_up", [sit(dy=-1), stand()], 8, False)
    add("scratch", [sit(eye="happy", mouth="pant", hx=29, hy=24, scratch=p) for p in ((26, 26), (27, 28), (26, 26), (27, 29))], 14)
    add("yawn", [sit(), sit(mouth="yawn_small", eye="half", ears="back"), sit(mouth="yawn", eye="closed", ears="back", hy=22),
                 sit(mouth="yawn", eye="closed", ears="back", hy=22), sit(mouth="yawn_small", eye="half"), sit(eye="half")], 5, False)
    add("lie_down", [sit(dy=1), lie()], 7, False)
    add("lie", [lie(), lie(dy=1)], 1.8)
    add("lie_blink", [lie(eye="closed")], 6, False)
    add("lie_look", [lie(eye="up", ears="perk")], 2)
    add("lie_sleepy", [lie(eye="half"), lie(eye="half", dy=1)], 1.5)
    add("get_up", [lie(dy=-1), stand(dy=1)], 7, False)
    add("sleep", [sleep(0), sleep(1)], 1.2)
    add("sleep_twitch", [sleep(0, ears="twitch"), sleep(1)], 6, False)
    add("wake", [sleep(0, ears="up"), lie(eye="half"), lie(eye="half")], 4, False)
    add("stretch", [bow(), bow(sway=1), bow(sway=-1), bow(sway=1), bow()], 5, False)
    rots = (-25, 25, -25, 25, 0)
    add("shake", [stand(rot=r, head_rot=r, eye="closed", ears="back", sway=(1 if r > 0 else -1), dy=0) for r in rots], 14, False)
    add("held", [held(s) for s in (0, 1, 0, -1)], 6)
    add("held_happy", [held(s, eye="happy", mouth="pant", ears="up") for s in (0, 1, 0, -1)], 6)
    add("fall", [fall(flap=0), fall(flap=1)], 10)
    add("land", [stand(dy=3, brx=12, bry=5, eye="closed", ears="back", hy=28), stand(dy=1)], 10, False)
    add("beg", [beg(0), beg(-1)], 5)
    add("belly", [belly(0), belly(1)], 4)
    add("front", [front(), front(dy=1)], 2)
    add("front_blink", [front(eye="closed")], 6, False)
    add("front_tilt_l", [front(tilt=1)], 2)
    add("front_tilt_r", [front(tilt=-1)], 2)
    add("front_happy", [front(eye="happy", mouth="pant"), front(eye="happy", mouth="pant", dy=1)], 5)
    add("front_look", [front(look=1)], 2)
    add("front_bark", [front(mouth="open", look=1), front(look=1)], 8, False)
    add("front_petted", [front(eye="happy", ears="back"), front(eye="happy", ears="back", dy=1)], 3)
    add("zoom", walk_frames(4, stride=4.2, lift=3.2, mouth="pant", run=True, ears="back", stretch=1.8), 20)
    add("skid", [tug(0, 0), tug(0, 1)], 12)
    add("tug", [tug(0, s) for s in (-1, 1)] + [tug(1, s) for s in (-1, 1)], 10)
    add("paw", [front(paw=1, mouth="pant", eye="happy"), front(paw=2, mouth="pant", eye="happy")], 5)
    add("side", [side_lie()], 2)
    add("roll", [lie(eye="closed"), side_lie(), belly(0), flipped(side_lie()), flipped(lie(eye="closed"))], 7, False)
    add("dead", [dead(0), dead(0), dead(1), dead(0)], 3)
    add("back", [back_view(), back_view(dy=1)], 3)
    add("spin", [stand(eye="happy", mouth="pant"), front(eye="happy", mouth="pant"), flipped(stand(eye="happy", mouth="pant")), back_view(sway=1)], 12)
    add("howl", [howl(0), howl(1), howl(1), howl(0)], 3)
    add("dream_run", [side_lie(p) for p in (0, 0.25, 0.5, 0.75)], 8)
    add("eat", [sniff(mouth="open", eye="closed"), sniff(dy=1, eye="closed"), sniff(mouth="open", eye="closed", twitch=0.5), sniff(dy=1, eye="closed")], 7)
    add("carry", [stand(mouth="hold", eye="happy", sway=s) for s in (-1, 1)], 6)
    add("walk_carry", walk_frames(6, mouth="hold"), 10)
    add("wink", [front(eye="wink", mouth="pant")], 2)
    add("peek", [peek(), peek(dy=1)], 1.6)
    add("peek_blink", [peek(eye="closed")], 6, False)
    add("peek_paw", [peek(paw=(39, 40)), peek(paw=(42, 46)), peek(paw=(41, 46)), peek(paw=(38, 42))], 8, False)
    add("look_down", [stand(eye="down", ears="perk", hy=28, hx=32), stand(eye="down", ears="perk", hy=28, hx=32, dy=1)], 2)
    add("surf", [surf(-8, -1), surf(8, 1)], 6)
    add("dizzy", [dizzy(s) for s in (-1, 0, 1, 0)], 4)
    return A


# ---------------------------------------------------------------- icons (emotes, ball)

ICONS = {
    "heart": """
.RR.RR.
RWRRRRR
RRRRRRR
.RRRRR.
..RRR..
...R...""",
    "question": """
.WWWW.
WW..WW
....WW
...WW.
..WW..
..WW..
......
..WW..
..WW..""",
    "exclaim": """
YY
YY
YY
YY
YY
..
YY
YY""",
    "z": """
WWWWW
...WW
..WW.
.WW..
WWWWW""",
    "note": """
..BBBB
..BBBB
..B..B
..B..B
BBB.BB
BBB.BB""",
    "bone": """
WW...WW
WWWWWWW
.WWWWW.
WWWWWWW
WW...WW""",
    "sweat": """
..C.
.CC.
CWCC
CCCC
.CC.""",
    "sparkle": """
...Y...
...Y...
..YWY..
YYWWWYY
..YWY..
...Y...
...Y...""",
    "puff": """
.GG.GG.
GWWGWWG
GWWWWWG
.GGGGG.""",
    "dots": """
WW.WW.WW
WW.WW.WW""",
    "ball": """
.RRR.
RWRRR
RRRRR
RRRRD
.RDD.""",
    "food": """
.YYYY.
YWYYYY
YYYYOY
.YYYY.""",
    "cat": """
G.....G
GG...GG
GGGGGGG
GEGGGEG
GGGPGGG
.GGGGG.""",
    "sock": """
RRR..
WWW..
RRR..
WWW..
RRRR.
WWWWW
.RRRR""",
    "stick": """
.....NN
....NN.
.N.NN..
..NN...
.NN....
NN.....""",
    "leash": """
.RRR...
R...R..
R...R..
.RRRR..
....R..
....RR.
.....RS
......S""",
    "dirt": """
.N..
NNN.
.N.N""",
    "paw": """
.N.N.
N...N
.NNN.
.NNN.""",
    "laser": """
.rRr.
rRWRr
RWWWR
rRWRr
.rRr.""",
    "dizzy": """
.YY..Y.
Y..Y...
Y.YY..Y
.Y...YY
..YY...
Y...YY.""",
    "shades": """
KKKKKKKKK
KKKK.KKKK
.KK...KK.""",
    "bang": """
Y..Y..Y
.YYWYY.
.YWWWY.
YWWWWWY
.YWWWY.
.YYWYY.
Y..Y..Y""",
    "mail": """
WWWWWWWWW.RR
WGWWWWWGWRRR
WWGWWWGWWRR.
WWWGWGWWWW..
WWWWGWWWWW..
WWWWWWWWWW..
WWWWWWWWWW..""",
}
NO_OUTLINE = {"laser"}
ICOL = {"O": (40, 20, 16, 255), "R": (230, 60, 70, 255), "W": (255, 255, 255, 255), "Y": (255, 204, 64, 255),
        "B": (80, 130, 235, 255), "C": (120, 200, 255, 255), "D": (170, 30, 45, 255), "G": (205, 212, 222, 255),
        "E": (110, 200, 90, 255), "P": (240, 140, 160, 255), "N": (150, 100, 55, 255), "S": (190, 190, 205, 255),
        "r": (255, 40, 40, 110), "K": (34, 32, 40, 255)}


def icon(art, size=16, outline=True):
    rows = [r for r in art.strip("\n").split("\n")]
    h, w = len(rows), max(len(r) for r in rows)
    grid_ = np.zeros((size, size, 4), np.uint8)
    ox, oy = (size - w) // 2, (size - h) // 2
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch in ICOL:
                grid_[oy + y, ox + x] = ICOL[ch]
    if outline:
        filled = grid_[:, :, 3] > 0
        out = np.zeros_like(filled)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                out |= sh(filled, dy, dx, False)
        out &= ~filled
        grid_[out] = ICOL["O"]
    return Image.fromarray(grid_, "RGBA")


def art_image(art, colors, outline=(40, 20, 16, 255), pad=1):
    rows = [r for r in art.strip("\n").split("\n")]
    h, w = len(rows), max(len(r) for r in rows)
    g = np.zeros((h + pad * 2, w + pad * 2, 4), np.uint8)
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch in colors:
                g[y + pad, x + pad] = colors[ch]
    if outline:
        filled = g[:, :, 3] > 0
        out = np.zeros_like(filled)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx and dy:
                    continue
                out |= sh(filled, dy, dx, False)
        out &= ~filled
        g[out] = outline
    return Image.fromarray(g, "RGBA")


HAT_COL = {"K": (34, 32, 40, 255), "k": (80, 78, 92, 255), "R": (205, 40, 55, 255), "W": (250, 250, 250, 255),
           "Y": (250, 200, 60, 255), "y": (255, 235, 140, 255), "P": (240, 100, 170, 255), "B": (80, 170, 240, 255),
           "G": (60, 170, 90, 255), "r": (160, 25, 40, 255)}
HATS = {
    "tophat": {"side": """
.kKKKKK.
.kKKKKK.
.kKKKKK.
.kKKKKK.
.RRRRRR.
KKKKKKKKK""", "front": """
.kKKKKK.
.kKKKKK.
.kKKKKK.
.kKKKKK.
.RRRRRR.
KKKKKKKK"""},
    "party": {"side": """
...y...
...P...
..BBB..
..PPP..
.BBBBB.
.PPPPP.""", "front": """
...y...
...P...
..BBB..
..PPP..
.BBBBB.
.PPPPP."""},
    "santa": {"side": """
WW.......
WrRR.....
..RRRR...
..RRRRR..
.RRRRRRR.
WWWWWWWWW""", "front": """
......WW
....RRrW
...RRRR.
..RRRRR.
.RRRRRR.
WWWWWWWW"""},
    "crown": {"side": """
Y..Y..Y
YY.Y.YY
YyYYYyY
YRYBYRY
YYYYYYY""", "front": """
Y..Y..Y
YY.Y.YY
YyYYYyY
YRYBYRY
YYYYYYY"""},
}


SHADES = {"side": """
KKKKKKK
..KKWKK
..KKKKK
...KKK.""", "front": """
KKKKKKKKKKKKKKKK
.KKWKKK..KKWKKK.
.KKKKKK..KKKKKK.
..KKKK....KKKK.."""}


def shades_frame(canvas):
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    a = canvas.anchor.get("eye")
    if a is None:
        return im
    x, y, kind = a
    img = art_image(SHADES[kind], HAT_COL, outline=None, pad=0)
    if kind == "side":
        cx = 4.5
        if canvas.anchor.get("_flipped"):
            img = img.transpose(Image.FLIP_LEFT_RIGHT)
            cx = img.width - 4.5
    else:
        cx = img.width / 2
    im.alpha_composite(img, (int(round(x - cx)), int(round(y - 1))))
    return im


def hat_frame(canvas, hat):
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    a = canvas.anchor.get("hat")
    if a is None:
        return im
    x, y, kind = a
    art = HATS[hat]["side" if kind == "side" else "front"]
    img = art_image(art, HAT_COL)
    if kind == "side" and canvas.anchor.get("_flipped"):
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
    im.alpha_composite(img, (int(round(x - img.width / 2)), int(round(y - img.height + 1))))
    return im


def prop_bed():
    c = np.zeros((24, 32, 4), np.uint8)
    im = Image.new("RGBA", (32, 24), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse([2, 5, 29, 18], fill=(55, 85, 150, 255), outline=(25, 35, 60, 255))
    d.ellipse([4, 7, 27, 15], fill=(75, 110, 180, 255))
    d.ellipse([1, 11, 30, 22], fill=(65, 100, 170, 255), outline=(25, 35, 60, 255))
    d.ellipse([5, 11, 26, 18], fill=(130, 170, 230, 255))
    d.ellipse([8, 12, 23, 16], fill=(155, 195, 245, 255))
    for x in (10, 15, 20):
        d.point((x, 13), fill=(255, 255, 255, 255))
    return im


def prop_bowl(full):
    im = Image.new("RGBA", (32, 24), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.polygon([(9, 17), (23, 17), (21, 22), (11, 22)], fill=(205, 60, 60, 255), outline=(90, 20, 25, 255))
    d.line([(9, 17), (23, 17)], fill=(250, 140, 140, 255))
    if full:
        for (x, y) in ((11, 16), (13, 15), (15, 16), (17, 15), (19, 16), (21, 16), (14, 14), (18, 14), (16, 13)):
            d.rectangle([x, y, x + 1, y + 1], fill=(160, 100, 50, 255))
            d.point((x, y), fill=(200, 140, 80, 255))
    return im


def prop_bubble():
    im = Image.new("RGBA", (32, 24), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    for (x0, y0, x1, y1) in ((6, 1, 28, 18), (4, 5, 14, 15), (20, 3, 30, 14)):
        d.ellipse([x0, y0, x1, y1], fill=(255, 255, 255, 235), outline=(60, 60, 80, 255))
    for (x0, y0, x1, y1) in ((6, 1, 28, 18), (4, 5, 14, 15), (20, 3, 30, 14)):
        d.ellipse([x0 + 1, y0 + 1, x1 - 1, y1 - 1], fill=(255, 255, 255, 235))
    d.ellipse([3, 18, 7, 21], fill=(255, 255, 255, 235), outline=(60, 60, 80, 255))
    d.ellipse([0, 21, 3, 23], fill=(255, 255, 255, 235), outline=(60, 60, 80, 255))
    return im


CRIT_COL = {"B": (90, 190, 240, 255), "W": (255, 255, 255, 255), "K": (40, 30, 30, 255), "b": (40, 110, 200, 255),
            "N": (130, 85, 50, 255), "E": (15, 10, 10, 255), "O": (235, 125, 50, 255), "L": (70, 50, 30, 255),
            "D": (95, 60, 35, 255), "Y": (240, 190, 60, 255)}
CRITTERS = {
    "fly0": """
BB...BB
BWB.BWB
BBBKBBB
.bBKBb.
.bb.bb.""",
    "fly1": """
.......
.BBKBB.
BWBKBWB
.bbKbb.
.......""",
    "fly2": """
..B.B..
.BWKWB.
..BKB..
...K...
.......""",
    "bird0": """
....NNN..
...NNNEN.
NN.NNNNYY
.NNNOON..
..NOOON..
...NN....
...L.L...""",
    "bird1": """
.........
NN.NNN...
.NNNONNN.
..NOOONEN
...NN..YY
...L.L...
.........""",
    "bird2": """
....NNN..
...NNNEN.
NN.NNNNYY
.NNNOON..
..NOOON..
...L.L...
.........""",
    "bird3": """
..D......
.DND.....
..NNNEN..
NNNNNNYY.
..NOON...
.........
.........""",
    "bird4": """
.........
..NNNEN..
NNNNNNYY.
.DNOON...
DND......
.........
.........""",
}


def export_extras():
    props = [("bed", prop_bed()), ("bowl_empty", prop_bowl(False)), ("bowl_full", prop_bowl(True)), ("bubble", prop_bubble())]
    sheet = Image.new("RGBA", (32 * len(props), 24), (0, 0, 0, 0))
    for i, (_, im) in enumerate(props):
        sheet.alpha_composite(im, (i * 32, 0))
    sheet.save(os.path.join(OUT, "props.png"))
    names = list(CRITTERS.keys())
    cs = Image.new("RGBA", (16 * len(names), 16), (0, 0, 0, 0))
    for i, n in enumerate(names):
        img = art_image(CRITTERS[n], CRIT_COL)
        cs.alpha_composite(img, (i * 16 + (16 - img.width) // 2, 16 - img.height))
    cs.save(os.path.join(OUT, "critters.png"))
    return [n for n, _ in props], names


# ---------------------------------------------------------------- export

def preview(frames, scale=6, cols=8, path="preview.png", labels=None):
    rows = (len(frames) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * W * scale, rows * H * scale), (70, 90, 110, 255))
    d = ImageDraw.Draw(sheet)
    for i, f in enumerate(frames):
        im = f.resize((W * scale, H * scale), Image.NEAREST)
        x, y = (i % cols) * W * scale, (i // cols) * H * scale
        d.line([(x, y + GROUND * scale), (x + W * scale, y + GROUND * scale)], fill=(90, 110, 130, 255))
        sheet.alpha_composite(im, (x, y))
        if labels:
            d.text((x + 4, y + 2), labels[i], fill=(255, 255, 255, 255))
    for i in range(cols + 1):
        d.line([(i * W * scale, 0), (i * W * scale, rows * H * scale)], fill=(50, 60, 80, 255))
    for j in range(rows + 1):
        d.line([(0, j * H * scale), (cols * W * scale, j * H * scale)], fill=(50, 60, 80, 255))
    sheet.save(path)


def export(A):
    frames, anchors, anims = [], [], {}
    for name, a in A.items():
        idx = []
        for c in a["frames"]:
            idx.append(len(frames))
            frames.append(c.image())
            m = c.anchor.get("mouth", (W / 2, H / 2))
            h = c.anchor.get("head", (W / 2, 6))
            anchors.append([round(m[0], 1), round(m[1], 1), round(h[0], 1), round(max(h[1], 0), 1)])
        anims[name] = dict(frames=idx, fps=a["fps"], loop=a["loop"])
    edge = []
    for name, a in A.items():
        for k, c in enumerate(a["frames"]):
            im = np.array(c.image())[:, :, 3] > 0
            if im[:, 0].any() or im[:, -1].any() or im[0, :].any():
                edge.append(f"{name}:{k}")
    if edge:
        print("WARNING frames touching the edge:", ", ".join(edge))
    cols = 16
    rows = (len(frames) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * W, rows * H), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        sheet.alpha_composite(f, ((i % cols) * W, (i // cols) * H))
    os.makedirs(OUT, exist_ok=True)
    sheet.save(os.path.join(OUT, "milo.png"))
    names = list(ICONS.keys())
    isheet = Image.new("RGBA", (16 * len(names), 16), (0, 0, 0, 0))
    for i, n in enumerate(names):
        isheet.alpha_composite(icon(ICONS[n], outline=n not in NO_OUTLINE), (i * 16, 0))
    isheet.save(os.path.join(OUT, "icons.png"))
    all_canvases = [c for a in A.values() for c in a["frames"]]
    for hat in HATS:
        hs = Image.new("RGBA", (cols * W, rows * H), (0, 0, 0, 0))
        for i, c in enumerate(all_canvases):
            hs.alpha_composite(hat_frame(c, hat), ((i % cols) * W, (i // cols) * H))
        hs.save(os.path.join(OUT, f"hat_{hat}.png"))
    ss = Image.new("RGBA", (cols * W, rows * H), (0, 0, 0, 0))
    for i, c in enumerate(all_canvases):
        ss.alpha_composite(shades_frame(c), ((i % cols) * W, (i // cols) * H))
    ss.save(os.path.join(OUT, "hat_shades.png"))
    props, critters = export_extras()
    meta = dict(frameW=W, frameH=H, ground=GROUND, columns=cols, anims=anims, anchors=anchors,
                icons=names, iconSize=16, hats=list(HATS.keys()) + ["shades"], props=props, propW=32, propH=24,
                critters=critters, critterSize=16)
    with open(os.path.join(OUT, "milo.json"), "w") as f:
        json.dump(meta, f, indent=1)
    return frames


if __name__ == "__main__":
    A = build()
    frames = export(A)
    labels = []
    for n, a in A.items():
        for i in range(len(a["frames"])):
            labels.append(f"{n}:{i}")
    prev = sys.argv[1] if len(sys.argv) > 1 else "/tmp/claude-0/sheet.png"
    preview(frames, path=prev, labels=labels, cols=10, scale=4)
    print(len(frames), "frames,", len(A), "anims")
