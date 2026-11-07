"""Bruit 3D vectorisé (numpy) : value noise à hash entier, fBm, ridged.

Tout est déterministe (aucun état global) : même entrée + même seed = même sortie,
ce qui garantit que le kit se régénère à l'identique.
Les points sont des tableaux (N, 3) ; les sorties sont des tableaux (N,) dans [0, 1].
"""
from __future__ import annotations

import numpy as np

_U32 = np.uint32
_INV_2_32 = np.float32(1.0 / 4294967296.0)


def _hash3(ix: np.ndarray, iy: np.ndarray, iz: np.ndarray, seed: int) -> np.ndarray:
    """Hash entier 32 bits (famille 'lowbias32') -> flottant dans [0, 1)."""
    salt = _U32((seed * 0x9E3779B1) & 0xFFFFFFFF)
    h = (ix.astype(np.uint32) * _U32(0x8DA6B343)) ^ (iy.astype(np.uint32) * _U32(0xD8163841)) \
        ^ (iz.astype(np.uint32) * _U32(0xCB1AB31F)) ^ salt
    h ^= h >> _U32(16)
    h *= _U32(0x7FEB352D)
    h ^= h >> _U32(15)
    h *= _U32(0x846CA68B)
    h ^= h >> _U32(16)
    return h.astype(np.float32) * _INV_2_32


def value_noise3(p: np.ndarray, seed: int = 0) -> np.ndarray:
    """Value noise 3D, interpolation quintique (pas de cassures de dérivée)."""
    p = np.asarray(p, dtype=np.float32)
    cell = np.floor(p)
    f = p - cell
    i = cell.astype(np.int32)
    u = f * f * f * (f * (f * np.float32(6.0) - np.float32(15.0)) + np.float32(10.0))
    ix, iy, iz = i[:, 0], i[:, 1], i[:, 2]
    ux, uy, uz = u[:, 0], u[:, 1], u[:, 2]

    def corner(dx: int, dy: int, dz: int) -> np.ndarray:
        return _hash3(ix + dx, iy + dy, iz + dz, seed)

    x00 = corner(0, 0, 0) + (corner(1, 0, 0) - corner(0, 0, 0)) * ux
    x10 = corner(0, 1, 0) + (corner(1, 1, 0) - corner(0, 1, 0)) * ux
    x01 = corner(0, 0, 1) + (corner(1, 0, 1) - corner(0, 0, 1)) * ux
    x11 = corner(0, 1, 1) + (corner(1, 1, 1) - corner(0, 1, 1)) * ux
    y0 = x00 + (x10 - x00) * uy
    y1 = x01 + (x11 - x01) * uy
    return y0 + (y1 - y0) * uz


def fbm3(p: np.ndarray, octaves: int = 4, seed: int = 0, lacunarity: float = 2.03, gain: float = 0.5) -> np.ndarray:
    """Somme d'octaves de value noise, normalisée dans [0, 1] (centrée vers 0.5)."""
    p = np.asarray(p, dtype=np.float32)
    total = np.zeros(len(p), dtype=np.float32)
    amplitude, norm = 1.0, 0.0
    for octave in range(octaves):
        offset = np.float32(17.31 * octave + 3.7)
        total += np.float32(amplitude) * value_noise3(p + offset, seed + octave * 7)
        norm += amplitude
        amplitude *= gain
        p = p * np.float32(lacunarity)
    return total / np.float32(norm)


def ridged(noise01: np.ndarray) -> np.ndarray:
    """Transforme un bruit [0,1] en arêtes : 1 sur la crête, 0 dans les creux."""
    return 1.0 - np.abs(noise01 * 2.0 - 1.0)


def smoothstep(edge0: float, edge1: float, x: np.ndarray) -> np.ndarray:
    """Équivalent HLSL smoothstep (edge0 > edge1 autorisé : courbe inversée)."""
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)
