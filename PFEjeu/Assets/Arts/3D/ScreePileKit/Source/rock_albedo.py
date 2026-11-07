"""Peinture procédurale de l'albédo (style stylisé / peint à la main, couleurs sRGB).

Entrées par texel : position de surface Q, normale géométrique n, concavité (AO analytique).
Couches : pierre (variations larges + facettes), strates (variante B), mousse sur les faces
tournées vers le haut, terre à la base, occlusion dans les creux, liseré clair sur les arêtes convexes.
"""
from __future__ import annotations

import numpy as np

from rock_noise import fbm3, smoothstep, value_noise3

_MOSS_DARK = np.array([0.19, 0.27, 0.11], dtype=np.float32)
_MOSS_LIGHT = np.array([0.33, 0.42, 0.16], dtype=np.float32)
_DIRT = np.array([0.22, 0.17, 0.11], dtype=np.float32)
_COOL = np.array([0.94, 0.97, 1.04], dtype=np.float32)
_WARM = np.array([1.06, 1.00, 0.92], dtype=np.float32)

AO_FULL_CONCAVITY = 0.045    # concavité (m) pour laquelle l'occlusion est maximale
EDGE_FULL_CONVEXITY = 0.040  # convexité (m) pour laquelle le liseré clair est maximal


def _posterize(x: np.ndarray, steps: int, strength: float) -> np.ndarray:
    """Mélange le signal continu avec une version en paliers : aspect peint, sans bandes dures."""
    stepped = (np.floor(x * steps) + 0.5) / steps
    return x + (stepped - x) * np.float32(strength)


def shade_albedo(spec, q: np.ndarray, normal: np.ndarray, concavity: np.ndarray) -> np.ndarray:
    """Retourne l'albédo (N, 3) float32 dans [0, 1] (valeurs sRGB)."""
    scale = np.float32(spec.scale)
    q = np.asarray(q, dtype=np.float32) / scale     # espace normalisé : mêmes motifs quelle que soit la taille
    concavity = concavity / scale
    ny = normal[:, 1]
    seed = spec.seed

    broad = fbm3(q * 2.4, 4, seed + 1)
    mid = fbm3(q * 9.0, 3, seed + 2)
    fine = value_noise3(q * 38.0, seed + 3)
    warm = fbm3(q * 1.4, 2, seed + 4)

    tone = 0.78 + 0.46 * _posterize(broad, 5, 0.35) + 0.22 * (mid - 0.5) + 0.10 * (fine - 0.5)
    color = np.array(spec.stone_color, dtype=np.float32)[None, :] * tone[:, None]
    color *= (_COOL[None, :] + (_WARM - _COOL)[None, :] * warm[:, None])
    color *= (0.88 + 0.22 * np.clip(ny, 0.0, 1.0))[:, None]                  # dessus un peu plus clair
    color *= (1.0 - 0.35 * smoothstep(-0.2, -0.8, ny))[:, None]              # dessous plus sombre

    if spec.strata is not None:
        band = 0.5 + 0.5 * np.sin(q[:, 1] / spec.strata[1] * 2.0 * np.pi + 2.0 * mid)
        color *= (1.0 + spec.strata_tone * (band - 0.5))[:, None]

    # Mousse : surfaces tournées vers le haut, bord franc et irrégulier.
    moss_field = 0.55 * ny + 1.0 * (fbm3(q * 4.5, 3, seed + 5) - 0.5) + 0.2 * (mid - 0.5)
    threshold = 0.70 - 0.25 * spec.moss_amount
    moss_mask = smoothstep(threshold, threshold + 0.06, moss_field) * smoothstep(0.08, 0.18, q[:, 1])
    moss_color = _MOSS_DARK[None, :] + (_MOSS_LIGHT - _MOSS_DARK)[None, :] * (0.25 * fine + 0.75 * mid)[:, None]
    color += (moss_color - color) * (0.88 * moss_mask)[:, None]

    # Terre accumulée au pied.
    dirt = smoothstep(0.20, 0.0, q[:, 1]) * (0.55 + 0.45 * mid)
    color += (_DIRT[None, :] - color) * (0.75 * dirt)[:, None]

    # Occlusion (creux) et liseré (arêtes convexes).
    occlusion = np.clip(concavity / AO_FULL_CONCAVITY, 0.0, 1.0)
    edge = np.clip(-concavity / EDGE_FULL_CONVEXITY, 0.0, 1.0)
    color *= (0.50 + 0.50 * (1.0 - 0.65 * occlusion))[:, None]
    color *= (1.0 + 0.22 * edge)[:, None]
    return np.clip(color, 0.0, 1.0).astype(np.float32)
