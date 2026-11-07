#!/usr/bin/env python3
"""
Generateur de pans de falaise modulaires (3 variantes A / B / C).

Principe
  - Un champ de hauteur haute definition (R x R) decrit le relief de la paroi.
  - Les bords (gauche/droite/haut/bas) suivent TOUS le meme champ periodique "B" :
    n'importe quelle variante se raccorde donc a n'importe quelle autre, horizontalement
    et verticalement (periode = largeur / hauteur du pan).
  - Le maillage low-poly (LOD0/1/2) est echantillonne sur ce champ ; les normales et couleurs
    des sommets de bord sont calculees analytiquement pour que les coutures soient exactes.
  - Le detail fin est "bake" dans une normal map (residu = champ HD - champ low-poly),
    et l'albedo contient l'AO / cavite.

Repere (Unity-friendly, metres, Y vers le haut) :
  X de -W/2 a +W/2, Y de 0 (pied du pan) a H, Z : face avant en relief (0 .. D_MAX), dos plat a -T.

Usage : python3 generate_cliff_pan.py <dossier_sortie>   (CLIFF_RES=1024|2048 pour la definition des textures)
"""
import os
import sys

import numpy as np
import scipy.ndimage as ndi
from PIL import Image
from scipy.spatial import cKDTree
from skimage.transform import resize

# --------------------------------------------------------------------------- parametres
R = int(os.environ.get("CLIFF_RES", 2048))     # definition du champ de hauteur et des textures
W, H, T = 4.0, 4.0, 0.6                        # largeur, hauteur, epaisseur du dos (m)
D_MAX = 0.95                                   # relief maximal de la face avant (m)
MEAN_D = 0.38                                  # relief moyen (m)
BORDER = 0.20                                  # largeur de la zone de raccord (m)
LOD_GRIDS = {0: 96, 1: 48, 2: 24}              # nombre de quads par cote
VARIANTS = ["A", "B", "C"]
NORMAL_STRENGTH = 1.2

PX = W / R
X1 = ((np.arange(R, dtype=np.float32) + 0.5) * PX - W / 2).astype(np.float32)
Y1 = (H - (np.arange(R, dtype=np.float32) + 0.5) * (H / R)).astype(np.float32)
X = X1[None, :]
Y = Y1[:, None]


# --------------------------------------------------------------------------- outils
def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def fbm(rng, cx, cy, octaves, gain=0.5, lac=2.0):
    """Bruit fractal lisse non periodique, valeurs ~[0,1]."""
    out = np.zeros((R, R), np.float32)
    amp, total = 1.0, 0.0
    for _ in range(octaves):
        grid = rng.random((max(2, int(round(cy))), max(2, int(round(cx))))).astype(np.float32)
        out += amp * resize(grid, (R, R), order=3, mode="reflect", anti_aliasing=False).astype(np.float32)
        total += amp
        amp *= gain
        cx *= lac
        cy *= lac
    return out / total


def blur(a, sigma_m):
    """Flou gaussien rapide (calcul a resolution reduite pour les grands rayons)."""
    sigma_px = sigma_m / PX
    f = 1
    while sigma_px / f > 8 and R % (f * 2) == 0:
        f *= 2
    if f == 1:
        return ndi.gaussian_filter(a, sigma_px)
    small = a.reshape(R // f, f, R // f, f).mean(axis=(1, 3))
    small = ndi.gaussian_filter(small, sigma_px / f)
    return ndi.zoom(small, f, order=1).astype(np.float32)


class PeriodicField:
    """Somme de cosinus a frequences entieres : periodique en X (periode W) et en Y (periode H)."""

    def __init__(self, seed, kmax=6, decay=1.6):
        rng = np.random.default_rng(seed)
        self.terms = []
        for kx in range(0, kmax + 1):
            for ky in range(-kmax, kmax + 1):
                if kx == 0 and ky <= 0:
                    continue
                k2 = kx * kx + ky * ky
                amp = rng.normal() / (k2 ** (0.5 * decay))
                self.terms.append((kx, ky, amp, rng.uniform(0, 2 * np.pi)))
        self.norm = 1.0 / np.sqrt(sum(a * a for _, _, a, _ in self.terms) / 2.0)

    def grid(self, x, y, deriv=None):
        """Evalue sur la grille (len(y), len(x)). deriv : None, 'x' ou 'y'."""
        out = np.zeros((len(y), len(x)), np.float32)
        for kx, ky, amp, ph in self.terms:
            a = 2 * np.pi * kx * x / W
            b = 2 * np.pi * ky * y / H + ph
            ca, sa, cb, sb = np.cos(a), np.sin(a), np.cos(b), np.sin(b)
            if deriv is None:
                out += amp * (np.outer(cb, ca) - np.outer(sb, sa))
            elif deriv == "x":
                out += -amp * (np.outer(cb, sa) + np.outer(sb, ca)) * (2 * np.pi * kx / W)
            else:
                out += -amp * (np.outer(cb, sa) + np.outer(sb, ca)) * (2 * np.pi * ky / H)
        return out * self.norm


FIELD_B = PeriodicField(seed=1001, kmax=10, decay=1.2)   # forme commune des bords (raccords)
FIELD_TONE = PeriodicField(seed=2002, kmax=5)    # teinte commune des bords
B_AMP = 0.085


def border_height(x, y, deriv=None):
    g = FIELD_B.grid(x, y, deriv)
    return (MEAN_D + B_AMP * g) if deriv is None else (B_AMP * g)


def voronoi(gx, gy, seed, warp_amp, jitter=0.9):
    """Cellules de Voronoi (gx x gy cellules). Retourne distances, cellule, coordonnees locales."""
    rng = np.random.default_rng(seed)
    u = ((X + W / 2) / W).astype(np.float32) * np.ones((R, 1), np.float32)
    v = ((H - Y) / H).astype(np.float32) * np.ones((1, R), np.float32)
    if warp_amp > 0:
        u = u + (fbm(rng, 3, 3, 3) - 0.5) * warp_amp
        v = v + (fbm(rng, 3, 3, 3) - 0.5) * warp_amp
    U, V = u * gx, v * gy
    pts = []
    for i in range(-2, gx + 2):
        for j in range(-2, gy + 2):
            pts.append((i + 0.5 + (rng.random() - 0.5) * jitter, j + 0.5 + (rng.random() - 0.5) * jitter))
    pts = np.array(pts, np.float32)
    tree = cKDTree(pts)
    dist, idx = tree.query(np.column_stack([U.ravel(), V.ravel()]), k=2, workers=-1)
    d1 = dist[:, 0].reshape(R, R).astype(np.float32)
    d2 = dist[:, 1].reshape(R, R).astype(np.float32)
    cell = idx[:, 0].reshape(R, R)
    lx = (U - pts[cell, 0]).astype(np.float32)
    ly = (V - pts[cell, 1]).astype(np.float32)
    return d1, d2, cell, lx, ly, len(pts), rng


# --------------------------------------------------------------------------- variantes
def variant_a():
    """A : strates horizontales (calcaire / gres) avec corniches et fissures verticales."""
    rng = np.random.default_rng(11)
    warp = fbm(rng, 3, 3, 3) - 0.5
    s = (Y + warp * 0.8) / 0.5
    k = np.floor(s)
    f = (s - k).astype(np.float32)
    ki = (k.astype(np.int32) + 16) % 64
    protrude = rng.random(64).astype(np.float32)[ki]
    tone = rng.random(64).astype(np.float32)[(ki * 5 + 3) % 64]

    face = 0.55 * protrude + 0.13 * smoothstep(0.0, 0.16, f) - 0.07 * f
    crack_noise = fbm(rng, 10, 1.6, 3)
    crack_zone = smoothstep(0.52, 0.68, fbm(rng, 3, 3, 2))          # les fissures n'existent que par zones
    crack = (1.0 - smoothstep(0.0, 0.014, np.abs(crack_noise - 0.5))) * crack_zone
    chips = 1.0 - np.abs(2.0 * fbm(rng, 22, 22, 4) - 1.0)           # bruit "ridge" : surfaces ebrechees
    relief = face + 0.12 * (fbm(rng, 40, 40, 4) - 0.5) + 0.07 * chips - 0.10 * crack
    aux = {"layer_tone": tone, "crack": crack, "ledge": smoothstep(0.0, 0.16, f)}
    return relief, aux, rng


def variant_b():
    """B : colonnes verticales (basalte) separees par des fissures, avec joints horizontaux."""
    d1, d2, cell, lx, ly, n, rng = voronoi(9, 2, seed=22, warp_amp=0.05)
    edge = d2 - d1
    cell_h = rng.random(n).astype(np.float32)
    cell_phase = rng.random(n).astype(np.float32)
    joint_rnd = rng.random(512).astype(np.float32)

    yj = Y * 0.9 + cell_phase[cell] * 5.0
    jk = np.floor(yj)
    jf = (yj - jk).astype(np.float32)
    jr = joint_rnd[(cell * 7 + jk.astype(np.int32)) % 512]
    tilt = (rng.random(n).astype(np.float32) - 0.5) * 0.30

    face = 0.60 * cell_h[cell] + 0.08 * smoothstep(0.05, 0.4, edge) + tilt[cell] * ly
    face += 0.09 * (jr - 0.5)
    joint = (1.0 - smoothstep(0.0, 0.04, jf)) * (jr > 0.5)          # un joint sur deux seulement
    face -= 0.07 * joint
    crevice = 1.0 - smoothstep(0.0, 0.10, edge)
    chips = 1.0 - np.abs(2.0 * fbm(rng, 24, 24, 4) - 1.0)
    relief = face - 0.24 * crevice + 0.06 * (fbm(rng, 40, 40, 4) - 0.5) + 0.07 * chips
    streak = fbm(rng, 40, 1.5, 3)
    aux = {"cell_tone": cell_h[cell], "crevice": crevice, "streak": streak}
    return relief, aux, rng


def variant_c():
    """C : gros blocs inclines avec un surplomb diagonal et de larges fractures."""
    d1, d2, cell, lx, ly, n, rng = voronoi(3, 4, seed=33, warp_amp=0.10)
    edge = d2 - d1
    h = rng.random(n).astype(np.float32)
    tx = (rng.random(n).astype(np.float32) - 0.5) * 0.35
    ty = (rng.random(n).astype(np.float32) - 0.5) * 0.35
    warp = fbm(rng, 3, 3, 3) - 0.5

    face = 1.1 * h[cell] + 0.9 * (tx[cell] * lx + ty[cell] * ly)
    crevice = 1.0 - smoothstep(0.0, 0.20, edge)
    lip = 0.16 * smoothstep(0.08, 0.45, edge)
    shelf = 0.30 * smoothstep(-0.12, 0.12, Y - (1.5 + 0.45 * np.sin(1.4 * X + 0.8) + warp * 1.2))
    chips = 1.0 - np.abs(2.0 * fbm(rng, 16, 16, 4) - 1.0)
    relief = face + lip - 0.42 * crevice + shelf + 0.10 * (fbm(rng, 10, 10, 4) - 0.5) + 0.09 * chips
    iron = smoothstep(0.58, 0.72, fbm(rng, 5, 5, 4))
    aux = {"block_tone": h[cell], "crevice": crevice, "iron": iron}
    return relief, aux, rng


# --------------------------------------------------------------------------- assemblage du champ
def edge_mask():
    e = np.minimum(np.minimum(X + W / 2, W / 2 - X), np.minimum(Y, H - Y))
    return (1.0 - smoothstep(0.0, BORDER, e)).astype(np.float32)


def build_height(relief, gain):
    v = MEAN_D + (relief - relief.mean()) * gain
    v = np.clip(v, 0.0, D_MAX).astype(np.float32)
    b = (MEAN_D + B_AMP * FIELD_B.grid(X1, Y1)).astype(np.float32)
    m = edge_mask()
    return (m * b + (1.0 - m) * v).astype(np.float32), m


def slope_up(hf):
    """Composante 'vers le haut' de la normale (moussse / depots sur les replats)."""
    gy = np.gradient(blur(hf, 0.02), PX, axis=0) * -1.0   # d/dy monde (les lignes vont vers le bas)
    gx = np.gradient(blur(hf, 0.02), PX, axis=1)
    return (-gy / np.sqrt(1.0 + gx * gx + gy * gy)).astype(np.float32)


def make_albedo(name, hf, m, aux):
    rng = np.random.default_rng(77 + ord(name))
    tone = 1.0 / (1.0 + np.exp(-1.6 * FIELD_TONE.grid(X1, Y1)))
    c_dark = np.array([0.30, 0.27, 0.24], np.float32)
    c_light = np.array([0.60, 0.55, 0.47], np.float32)
    base = c_dark[None, None, :] + (c_light - c_dark)[None, None, :] * tone[..., None]

    col = base.copy()
    up = slope_up(hf)
    moss_noise = fbm(rng, 14, 14, 4)

    if name == "A":
        lm = 0.80 + 0.40 * aux["layer_tone"]
        warm = 0.90 + 0.20 * aux["layer_tone"][..., None] * np.array([1.0, 0.0, -1.0], np.float32)[None, None, :]
        col = base * lm[..., None] * warm
        col *= (1.0 - 0.35 * aux["crack"])[..., None]
        col += 0.05 * aux["ledge"][..., None]
        moss = smoothstep(0.35, 0.60, up) * smoothstep(0.56, 0.70, moss_noise) * 0.5
    elif name == "B":
        col = base * (0.58 + 0.12 * (aux["cell_tone"] - 0.5))[..., None]
        col *= np.array([0.95, 1.0, 1.06], np.float32)[None, None, :]
        col += 0.16 * smoothstep(0.55, 0.80, aux["streak"])[..., None] * 0.6
        moss = smoothstep(0.35, 0.6, up) * smoothstep(0.55, 0.70, moss_noise) * 0.7
    else:
        col = base * (0.92 + 0.16 * (aux["block_tone"] - 0.5))[..., None]
        iron = aux["iron"] * 0.6
        col = col * (1.0 - iron[..., None]) + np.array([0.52, 0.30, 0.17], np.float32)[None, None, :] * iron[..., None]
        moss = smoothstep(0.30, 0.55, up) * smoothstep(0.52, 0.66, moss_noise)

    moss_col = np.array([0.24, 0.31, 0.13], np.float32)[None, None, :]
    col = col * (1.0 - 0.8 * moss[..., None]) + moss_col * 0.8 * moss[..., None]

    # les bords reprennent la teinte commune : meme couleur d'un pan a l'autre
    col = col * (1.0 - m[..., None]) + base * m[..., None]

    # AO / cavite + liseres de convexite
    small = blur(hf, 0.06)
    large = blur(hf, 0.25)
    cavity = np.clip((small - hf) / 0.05, 0, 1)
    ao_large = np.clip((large - hf) / 0.20, 0, 1) * 0.30
    convex = np.clip((hf - small) / 0.04, 0, 1) * 0.10
    col = col * (1.0 - 0.55 * cavity - ao_large)[..., None] * (1.0 + convex)[..., None]
    col += (rng.random((R, R, 1)).astype(np.float32) - 0.5) * 0.04
    return np.clip(col, 0, 1)


def make_normal(hf):
    """Normal map tangent-space (OpenGL, Y+) du residu : detail que le maillage low-poly ne porte pas."""
    residual = hf - blur(hf, 0.03)
    gx = np.gradient(residual, PX, axis=1)
    gy_img = np.gradient(residual, PX, axis=0)
    n = np.stack([-gx * NORMAL_STRENGTH, gy_img * NORMAL_STRENGTH, np.ones_like(gx)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


# --------------------------------------------------------------------------- maillage
def build_mesh(hf, n_cells):
    """Retourne vertices, normales, uv, faces d'un LOD. Face avant + dos + 4 flancs (watertight)."""
    sigma = 0.5 * (W / n_cells)
    hs = blur(hf, sigma)
    xs = np.linspace(-W / 2, W / 2, n_cells + 1).astype(np.float32)
    ys = np.linspace(0.0, H, n_cells + 1).astype(np.float32)
    rows = (H - ys) / H * R - 0.5
    cols = (xs + W / 2) / W * R - 0.5
    rr, cc = np.meshgrid(rows, cols, indexing="ij")
    z = ndi.map_coordinates(hs, [rr, cc], order=1, mode="nearest").astype(np.float32)

    ring = np.zeros_like(z, bool)
    ring[0, :] = ring[-1, :] = ring[:, 0] = ring[:, -1] = True
    z_b = border_height(xs, ys)
    z[ring] = z_b[ring]

    dx, dy = W / n_cells, H / n_cells
    gx = np.gradient(z, dx, axis=1)
    gy = np.gradient(z, dy, axis=0)
    gx_b, gy_b = border_height(xs, ys, "x"), border_height(xs, ys, "y")
    gx[ring], gy[ring] = gx_b[ring], gy_b[ring]
    nrm = np.stack([-gx, -gy, np.ones_like(gx)], axis=-1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)

    gxm, gym = np.meshgrid(xs, ys, indexing="xy")
    verts = np.stack([gxm, gym, z], axis=-1).reshape(-1, 3)
    normals = nrm.reshape(-1, 3)
    uvs = np.stack([(gxm + W / 2) / W, gym / H], axis=-1).reshape(-1, 2)

    n1 = n_cells + 1
    ii, jj = np.meshgrid(np.arange(n_cells), np.arange(n_cells), indexing="xy")
    v00 = (jj * n1 + ii).ravel()
    v10, v01, v11 = v00 + 1, v00 + n1, v00 + n1 + 1
    flip = ((ii + jj) % 2 == 1).ravel()
    tri_a = np.where(flip[:, None], np.stack([v00, v10, v01], 1), np.stack([v00, v10, v11], 1))
    tri_b = np.where(flip[:, None], np.stack([v10, v11, v01], 1), np.stack([v00, v11, v01], 1))
    faces = np.concatenate([tri_a, tri_b], axis=0)

    V, N, UV, F = [verts], [normals], [uvs], [faces]
    offset = len(verts)

    def add_strip(front_pts, outward):
        nonlocal offset
        back_pts = front_pts.copy()
        back_pts[:, 2] = -T
        pts = np.concatenate([front_pts, back_pts], axis=0)
        n = len(front_pts)
        uv = np.stack([(pts[:, 0] + W / 2) / W, pts[:, 1] / H], axis=-1)
        a = np.arange(n - 1)
        tris = np.concatenate([np.stack([a, a + 1, a + n + 1], 1), np.stack([a, a + n + 1, a + n], 1)], axis=0)
        p0, p1, p2 = pts[tris[0, 0]], pts[tris[0, 1]], pts[tris[0, 2]]
        if np.dot(np.cross(p1 - p0, p2 - p0), outward) < 0:
            tris = tris[:, ::-1]
        V.append(pts)
        N.append(np.tile(np.array(outward, np.float32), (len(pts), 1)))
        UV.append(uv)
        F.append(tris + offset)
        offset += len(pts)

    bottom = np.stack([xs, np.zeros_like(xs), z[0, :]], axis=-1)
    top = np.stack([xs, np.full_like(xs, H), z[-1, :]], axis=-1)
    left = np.stack([np.full_like(ys, -W / 2), ys, z[:, 0]], axis=-1)
    right = np.stack([np.full_like(ys, W / 2), ys, z[:, -1]], axis=-1)
    add_strip(bottom, (0, -1, 0))
    add_strip(top, (0, 1, 0))
    add_strip(left, (-1, 0, 0))
    add_strip(right, (1, 0, 0))

    # dos : eventail depuis le centre vers chaque sommet de bordure (pas de T-junction avec les flancs)
    loop = [(x, 0.0) for x in xs]
    loop += [(W / 2, y) for y in ys[1:]]
    loop += [(x, H) for x in xs[-2::-1]]
    loop += [(-W / 2, y) for y in ys[-2:0:-1]]
    ring_pts = np.array([[x, y, -T] for x, y in loop], np.float32)
    back = np.concatenate([np.array([[0.0, H / 2, -T]], np.float32), ring_pts], axis=0)
    m = len(ring_pts)
    k = np.arange(m)
    back_tris = np.stack([np.zeros(m, np.int64), 1 + k, 1 + (k + 1) % m], axis=1)
    q0, q1, q2 = back[back_tris[0]]
    if np.cross(q1 - q0, q2 - q0)[2] > 0:       # la normale doit regarder vers -Z
        back_tris = back_tris[:, ::-1]
    V.append(back)
    N.append(np.tile(np.array([0, 0, -1], np.float32), (len(back), 1)))
    UV.append(np.stack([(back[:, 0] + W / 2) / W, back[:, 1] / H], axis=-1))
    F.append(back_tris + offset)

    return (np.concatenate(V).astype(np.float32), np.concatenate(N).astype(np.float32),
            np.concatenate(UV).astype(np.float32), np.concatenate(F).astype(np.int64))


def write_obj(path, mtl_name, objects):
    """objects : liste (nom, materiau, V, N, UV, F). Un fichier .obj par variante, un objet par LOD."""
    with open(path, "w") as fh:
        fh.write(f"# Pan de falaise modulaire - unites : metres, Y haut, pivot au pied du pan\nmtllib {mtl_name}\n")
        off = 1
        for name, mat, V, N, UV, F in objects:
            fh.write(f"o {name}\nusemtl {mat}\n")
            fh.write("".join(f"v {a:.5f} {b:.5f} {c:.5f}\n" for a, b, c in V))
            fh.write("".join(f"vt {a:.5f} {b:.5f}\n" for a, b in UV))
            fh.write("".join(f"vn {a:.4f} {b:.4f} {c:.4f}\n" for a, b, c in N))
            fh.write("".join(f"f {a + off}/{a + off}/{a + off} {b + off}/{b + off}/{b + off} {c + off}/{c + off}/{c + off}\n" for a, b, c in F))
            off += len(V)


def save_png(arr01, path):
    Image.fromarray((np.clip(arr01, 0, 1) * 255 + 0.5).astype(np.uint8)).save(path, optimize=True)


# --------------------------------------------------------------------------- main
def main(out_dir):
    os.makedirs(os.path.join(out_dir, "Models"), exist_ok=True)
    os.makedirs(os.path.join(out_dir, "Textures"), exist_ok=True)
    os.makedirs(os.path.join(out_dir, "_work"), exist_ok=True)
    gens = {"A": (variant_a, 1.0), "B": (variant_b, 1.0), "C": (variant_c, 0.9)}
    report = {}
    for name in VARIANTS:
        func, gain = gens[name]
        print(f"[{name}] relief...", flush=True)
        relief, aux, _ = func()
        hf, m = build_height(relief, gain)
        np.save(os.path.join(out_dir, "_work", f"height_{name}.npy"), hf.astype(np.float16))
        print(f"[{name}] albedo / normal...", flush=True)
        save_png(make_albedo(name, hf, m, aux), os.path.join(out_dir, "Textures", f"CliffPan_{name}_D.png"))
        save_png(make_normal(hf), os.path.join(out_dir, "Textures", f"CliffPan_{name}_N.png"))
        print(f"[{name}] maillage...", flush=True)
        objs = []
        for lod, n_cells in LOD_GRIDS.items():
            V, N, UV, F = build_mesh(hf, n_cells)
            objs.append((f"CliffPan_{name}_LOD{lod}", f"CliffPan_{name}_Mat", V, N, UV, F))
            report[f"{name}_LOD{lod}"] = {"verts": len(V), "tris": len(F)}
            if lod == 0:
                np.savez_compressed(os.path.join(out_dir, "_work", f"mesh_{name}_lod0.npz"), V=V, N=N, UV=UV, F=F)
        mtl = f"CliffPan_{name}.mtl"
        with open(os.path.join(out_dir, "Models", mtl), "w") as fh:
            fh.write(f"newmtl CliffPan_{name}_Mat\nKd 1 1 1\nKs 0 0 0\nmap_Kd ../Textures/CliffPan_{name}_D.png\n"
                     f"map_Bump ../Textures/CliffPan_{name}_N.png\n")
        write_obj(os.path.join(out_dir, "Models", f"CliffPan_{name}.obj"), mtl, objs)
        print(f"[{name}] ok  relief min/max = {hf.min():.2f}/{hf.max():.2f} m", flush=True)
    # test de raccord : colonnes de bord de chaque variante
    edge_cols = {n: np.load(os.path.join(out_dir, "_work", f"height_{n}.npy")).astype(np.float32) for n in VARIANTS}
    worst = 0.0
    for a in VARIANTS:
        for b in VARIANTS:
            worst = max(worst, float(np.abs(edge_cols[a][:, -1] - edge_cols[b][:, 0]).max()))
            worst = max(worst, float(np.abs(edge_cols[a][0, :] - edge_cols[b][-1, :]).max()))
    report["max_seam_gap_m"] = worst
    print("RAPPORT", report)


if __name__ == "__main__":
    main(sys.argv[1])
