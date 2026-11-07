"""Forme analytique d'un éboulis : un monticule bas portant des pierres facettées.

Surface étoilée autour d'un centre O (comme les autres kits) :

    R(d) = max( monticule(d), max_i sortie_du_rayon_dans_pierre_i(d) ) + déplacement 3D

 - monticule : ellipsoïde posé au sol (la terre / le gravier entre les pierres) ;
 - pierre i : polytope (6 faces de boîte + plans de coupe tangents à un ellipsoïde), orienté au hasard ;
   l'intersection rayon/polytope est analytique (méthode des dalles), seules les pierres dans le cône
   du rayon sont évaluées (test de sphère englobante) ;
 - déplacement : bruit de valeur 3D en position (chaque pierre a son propre relief, pas de motif radial).

Même interface que RockShape (center, spec, clipped_radius, surface_points), plus shade() pour l'albédo
par pierre, ce qui permet de réutiliser le pipeline de bake tel quel.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import List, Tuple

import numpy as np

from rock_albedo import shade_albedo
from rock_noise import fbm3, ridged, smoothstep, value_noise3

Vec3 = Tuple[float, float, float]


@dataclass(frozen=True)
class ScreeSpec:
    key: str
    label: str
    seed: int
    mound_extents: Vec3                 # demi-axes du monticule (x, hauteur, z)
    center_y: float                     # hauteur du centre O (doit rester sous le sommet du monticule)
    stone_count: int
    size_range: Tuple[float, float]     # demi-taille d'une pierre (m) : petites au sommet, grosses en bas
    extra_stones: Tuple[Tuple[float, float, float], ...] = ()   # (x, z, demi-taille) imposées
    roughness: float = 1.0
    hard_edge_deg: float = 40.0
    scale: float = 0.8                  # normalise le bruit d'albédo (voir rock_albedo)
    stone_color: Vec3 = (0.50, 0.47, 0.43)
    moss_amount: float = 0.15
    strata = None                       # attribut lu par shade_albedo (pas de strates ici)
    strata_tone = 0.0


SCREE_SPECS: Tuple[ScreeSpec, ...] = (
    ScreeSpec(key="A", label="Éboulis dense", seed=211, mound_extents=(1.9, 0.62, 1.55), center_y=0.34,
              stone_count=44, size_range=(0.14, 0.34), extra_stones=((-0.55, 0.35, 0.50), (0.7, -0.4, 0.44)),
              roughness=1.0),
    ScreeSpec(key="B", label="Éboulis étalé", seed=233, mound_extents=(2.4, 0.42, 1.25), center_y=0.26,
              stone_count=38, size_range=(0.12, 0.30), extra_stones=((1.35, 0.1, 0.55),),
              roughness=0.9, stone_color=(0.54, 0.50, 0.44)),
)

_PLANES_RANDOM = 8
_STONE_PALETTE = np.array([[0.88, 0.88, 0.90], [1.12, 1.02, 0.90], [0.74, 0.74, 0.76],
                           [1.00, 0.94, 0.84], [0.82, 0.84, 0.90], [1.16, 1.10, 1.00]], dtype=np.float32)
_GRAVEL = np.array([0.40, 0.36, 0.30], dtype=np.float32)
_SOIL = np.array([0.24, 0.19, 0.13], dtype=np.float32)
_NOISE_FREQ = ((2.5, 0.045), (6.0, 0.022), (14.0, 0.009))
_CHIP = (9.0, 0.020)


def _random_rotation(rng: np.random.Generator) -> np.ndarray:
    yaw = rng.uniform(0, 2 * np.pi)
    tilt_x, tilt_z = rng.uniform(-0.45, 0.45, size=2)
    cy, sy, cx, sx, cz, sz = np.cos(yaw), np.sin(yaw), np.cos(tilt_x), np.sin(tilt_x), np.cos(tilt_z), np.sin(tilt_z)
    ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return ry @ rx @ rz


@dataclass
class _Stone:
    center: np.ndarray          # (3,)
    normals: np.ndarray         # (K, 3)
    heights: np.ndarray         # (K,)
    bound_radius: float


class ScreeShape:
    """Évalue la forme d'un éboulis (même contrat que RockShape)."""

    def __init__(self, spec: ScreeSpec):
        self.spec = spec
        self.center = np.array([0.0, spec.center_y, 0.0], dtype=np.float32)
        rng = np.random.default_rng(spec.seed)
        self._stones = self._build_stones(spec, rng)
        self._noise_offsets = rng.uniform(0.0, 100.0, size=(len(_NOISE_FREQ) + 1, 3)).astype(np.float32)
        self._mound = np.array(spec.mound_extents, dtype=np.float32)

    # ------------------------------------------------------------------ construction
    def _surface_height(self, x: float, z: float) -> float:
        a, b, c = self.spec.mound_extents
        inside = 1.0 - (x / a) ** 2 - (z / c) ** 2
        return b * np.sqrt(max(inside, 0.0))

    def _build_stones(self, spec: ScreeSpec, rng: np.random.Generator) -> List[_Stone]:
        a, _, c = spec.mound_extents
        placements = [(x, z, s) for x, z, s in spec.extra_stones]
        for _ in range(spec.stone_count):
            radial = rng.uniform(0.0, 1.0) ** 0.6 * 0.97
            angle = rng.uniform(0.0, 2 * np.pi)
            x, z = radial * a * np.cos(angle), radial * c * np.sin(angle)
            size = spec.size_range[0] + (spec.size_range[1] - spec.size_range[0]) * (0.25 + 0.75 * radial) * rng.uniform(0.7, 1.15)
            placements.append((x, z, size))

        stones = []
        for x, z, size in placements:
            extents = size * np.array([rng.uniform(0.9, 1.25), rng.uniform(0.55, 0.85), rng.uniform(0.8, 1.1)])
            y = self._surface_height(x, z) * 0.85 + 0.15 * extents[1]
            rotation = _random_rotation(rng)
            box = np.concatenate([np.eye(3), -np.eye(3)])
            random_n = rng.normal(size=(_PLANES_RANDOM, 3))
            random_n /= np.linalg.norm(random_n, axis=1, keepdims=True)
            local = np.concatenate([box, random_n])
            support = np.linalg.norm(local * extents, axis=1)
            cuts = np.concatenate([np.full(6, 1.0), rng.uniform(0.78, 0.95, size=_PLANES_RANDOM)])
            normals = local @ rotation.T                                  # local -> monde
            stones.append(_Stone(np.array([x, y, z]), normals.astype(np.float32),
                                 (support * cuts).astype(np.float32), float(np.linalg.norm(extents) * 1.05)))
        return stones

    # ------------------------------------------------------------------ intersections
    def _mound_exit(self, d: np.ndarray) -> np.ndarray:
        e2 = self._mound ** 2
        o = self.center
        a = np.sum(d * d / e2, axis=1)
        b = 2.0 * np.sum(o * d / e2, axis=1)
        c = np.sum(o * o / e2) - 1.0
        return (-b + np.sqrt(np.maximum(b * b - 4.0 * a * c, 0.0))) / (2.0 * a)

    def _stone_exit(self, d: np.ndarray, stone: _Stone, active: np.ndarray) -> np.ndarray:
        """Rayon -> sortie du polytope (méthode des dalles), évalué sur les rayons `active` seulement."""
        dd = d[active]
        denom = dd @ stone.normals.T                                     # (M, K)
        numer = stone.heights - (self.center - stone.center) @ stone.normals.T   # (K,) : h - n.(O - c)
        with np.errstate(divide="ignore", invalid="ignore"):
            t = numer / denom
        leaving = denom > 1e-9
        entering = denom < -1e-9
        parallel_outside = (~leaving) & (~entering) & (numer < 0.0)
        t_exit = np.where(leaving, t, np.inf).min(axis=1)
        t_enter = np.where(entering, t, -np.inf).max(axis=1)
        hit = (t_enter < t_exit) & (t_exit > 0.0) & (~parallel_outside.any(axis=1))
        return np.where(hit, t_exit, 0.0)

    def _union(self, d: np.ndarray, want_ids: bool = False):
        best = self._mound_exit(d)
        ids = np.full(len(d), -1, dtype=np.int32)
        for index, stone in enumerate(self._stones):
            to_stone = stone.center - self.center
            distance = float(np.linalg.norm(to_stone))
            if distance <= stone.bound_radius:
                active = np.ones(len(d), dtype=bool)
            else:
                cos_limit = np.sqrt(1.0 - (stone.bound_radius / distance) ** 2)
                active = (d @ (to_stone / distance)) >= cos_limit
            if not active.any():
                continue
            t = self._stone_exit(d, stone, active)
            current = best[active]
            better = t > current
            if better.any():
                where = np.nonzero(active)[0][better]
                best[where] = t[better]
                ids[where] = index
        return (best, ids) if want_ids else best

    # ------------------------------------------------------------------ API publique
    def _displacement(self, p0: np.ndarray, full_detail: bool) -> np.ndarray:
        rough = np.float32(self.spec.roughness)
        count = len(_NOISE_FREQ) if full_detail else 1
        disp = np.zeros(len(p0), dtype=np.float32)
        for i in range(count):
            freq, amp = _NOISE_FREQ[i]
            disp += np.float32(amp) * rough * (value_noise3(p0 * np.float32(freq) + self._noise_offsets[i], self.spec.seed + i) * 2.0 - 1.0)
        if full_detail:
            freq, amp = _CHIP
            chips = ridged(value_noise3(p0 * np.float32(freq) + self._noise_offsets[-1], self.spec.seed + 40))
            disp += np.float32(amp) * rough * (chips - 0.55) * 2.0
        return disp

    def radius(self, d: np.ndarray, full_detail: bool = True) -> np.ndarray:
        d = np.asarray(d, dtype=np.float32)
        radius = self._union(d)
        return radius + self._displacement(self.center + d * radius[:, None], full_detail)

    def clipped_radius(self, d: np.ndarray, full_detail: bool = True) -> Tuple[np.ndarray, np.ndarray]:
        d = np.asarray(d, dtype=np.float32)
        radius = self.radius(d, full_detail)
        downward = d[:, 1] < -1e-6
        limit = np.full(len(d), np.inf, dtype=np.float32)
        limit[downward] = self.center[1] / (-d[downward, 1])
        return np.minimum(radius, limit), radius > limit

    def surface_points(self, d: np.ndarray, full_detail: bool = True) -> Tuple[np.ndarray, np.ndarray]:
        radius, on_base = self.clipped_radius(d, full_detail)
        return self.center + np.asarray(d, dtype=np.float32) * radius[:, None], on_base

    # ------------------------------------------------------------------ albédo
    def shade(self, q: np.ndarray, normal: np.ndarray, concavity: np.ndarray, d: np.ndarray) -> np.ndarray:
        """Albédo : teinte propre à chaque pierre, gravier et terre entre les pierres."""
        _, ids = self._union(np.asarray(d, dtype=np.float32), want_ids=True)
        base = shade_albedo(self.spec, q, normal, concavity)
        palette_index = np.where(ids >= 0, (ids * 7 + self.spec.seed) % len(_STONE_PALETTE), 0)
        tint = _STONE_PALETTE[palette_index]
        color = base * tint

        scale = np.float32(self.spec.scale)
        qn = np.asarray(q, dtype=np.float32) / scale
        grain = value_noise3(qn * 46.0, self.spec.seed + 61)
        patch = fbm3(qn * 3.2, 3, self.spec.seed + 62)
        gravel = _GRAVEL[None, :] * (0.72 + 0.5 * grain + 0.25 * (patch - 0.5))[:, None]
        soil_mix = smoothstep(0.55, 0.30, patch)[:, None] * 0.55
        gravel = gravel + (_SOIL[None, :] - gravel) * soil_mix
        between = (ids < 0)[:, None]
        color = np.where(between, gravel * (0.6 + 0.4 * (1.0 - np.clip(concavity / 0.06, 0.0, 1.0)))[:, None], color)
        return np.clip(color, 0.0, 1.0).astype(np.float32)
