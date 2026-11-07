#!/usr/bin/env python3
"""Générateur du kit "Éboulis" : 2 variantes optimisées, 3 LODs, 1 atlas (1 matériau).

Usage :  python3 generate_scree.py <dossier_sortie> [--res 2048] [--quick]
Dépendances : numpy scipy pillow trimesh xatlas fast-simplification

Chaîne :
  forme analytique (rock_shapes) -> échantillonnage sphérique -> décimation quadrique (étanche)
  -> normales par coin (arêtes vives > 35°) -> dépliage UV xatlas des 15 maillages dans UN atlas
     (la base plate, enterrée, est exclue de l'atlas)
  -> rastérisation UV -> bake par texel : normale tangent-space (différences finies sur la forme HD)
     + albédo peint + AO analytique
  -> export OBJ (un fichier par variante, objets _LOD0/_LOD1/_LOD2) + PNG + rapport JSON.

        Atlas 2048²
        ┌──────────────────────────┐
        │ A_LOD0 B_LOD0 C_LOD0 ... │   densité 1.0
        │ A_LOD1 ...               │   densité 0.55
        │ A_LOD2 ...               │   densité 0.32
        └──────────────────────────┘
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
from collections import defaultdict
from dataclasses import dataclass
from typing import Dict, List, Tuple

import fast_simplification as fs
import numpy as np
import trimesh
import xatlas
from PIL import Image
from scipy import ndimage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scree_shapes import SCREE_SPECS as ROCK_SPECS, ScreeShape as RockShape  # noqa: E402

# ----------------------------------------------------------------------------- réglages
LOD_TARGET_TRIS = {0: 1500, 1: 700, 2: 250}
LOD_UV_SCALE = {0: 1.00, 1: 0.55, 2: 0.32}   # densité de texels relative dans l'atlas
ICO_SUBDIVISIONS = 6                          # 81 920 triangles de départ
NORMAL_EPS = 0.0018                           # pas angulaire (rad) des différences finies
AO_RADII = ((0.10, 0.6), (0.28, 0.4))         # (rayon angulaire en rad, poids)
AO_SAMPLES = 6
CHUNK = 300_000
HARD_EDGE_DEG = 35.0                          # au-delà : arête vive (normales séparées)
BASE_NORMAL_Y = -0.9                          # triangle de base : normale vers le bas
BASE_HEIGHT_EPS = 1e-4
PADDING_TEXELS = 6
DILATE_TEXELS = 8
RASTER_TOLERANCE = 0.0                        # tolérance barycentrique : 0 = aucun débordement hors du triangle
FLAT_NORMAL = (128, 128, 255)
FLAT_ALBEDO = (110, 104, 96)
MATERIAL_NAME = "ScreePile"


@dataclass
class LodMesh:
    variant: int
    lod: int
    vertices: np.ndarray        # (V, 3) float64, non dupliqués
    faces: np.ndarray           # (F, 3) int
    corner_normals: np.ndarray  # (F, 3, 3) normales par coin (arêtes vives > HARD_EDGE_DEG)
    is_base: np.ndarray         # (F,) True pour les triangles de la base plate (exclus de l'atlas)


@dataclass
class UnwrappedMesh:
    variant: int
    lod: int
    vertices: np.ndarray    # (V', 3) dupliqués aux coutures UV / arêtes vives
    normals: np.ndarray     # (V', 3)
    tangents: np.ndarray    # (V', 3) tangentes moyennées par sommet (approximation de MikkTSpace)
    faces: np.ndarray       # (F, 3)
    face_sign: np.ndarray   # (F,) signe de la bitangente (±1)
    is_base: np.ndarray     # (F,)
    uv_atlas: np.ndarray    # (V', 2) coordonnées dans l'image (v vers le bas = ligne d'image)


# ============================================================================= maillage
def _decimate(vertices: np.ndarray, faces: np.ndarray, target: int) -> trimesh.Trimesh:
    """Décimation quadrique ; on cherche une variante étanche et cohérente (cible légèrement ajustée sinon)."""
    best = None
    for attempt in range(14):
        for agg in (7, 5, 9):
            points, tris = fs.simplify(vertices, faces, target_count=target + attempt * 6, agg=agg)
            points = np.array(points)
            points[:, 1] = np.maximum(points[:, 1], 0.0)             # base exactement plate
            mesh = trimesh.Trimesh(points, tris, process=True)
            mesh.remove_unreferenced_vertices()
            best = mesh
            if mesh.is_watertight and mesh.is_winding_consistent and mesh.volume > 0:
                return mesh
    print("  [!] maillage non étanche malgré les tentatives", file=sys.stderr)
    return best


def compute_corner_normals(vertices: np.ndarray, faces: np.ndarray, angle_deg: float) -> np.ndarray:
    """Normale par coin de triangle : moyenne (pondérée par l'aire) des triangles voisins du sommet
    dont la normale reste à moins de angle_deg ; au-delà c'est une arête vive."""
    tri = vertices[faces]
    area_normal = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    length = np.linalg.norm(area_normal, axis=1, keepdims=True)
    unit = area_normal / np.maximum(length, 1e-15)

    faces_of_vertex: Dict[int, List[int]] = defaultdict(list)
    for f, face in enumerate(faces):
        for v in face:
            faces_of_vertex[int(v)].append(f)

    cos_limit = np.cos(np.radians(angle_deg))
    out = np.zeros((len(faces), 3, 3))
    for f, face in enumerate(faces):
        for corner, v in enumerate(face):
            neighbours = [g for g in faces_of_vertex[int(v)] if np.dot(unit[f], unit[g]) >= cos_limit]
            summed = area_normal[neighbours].sum(axis=0)
            norm = np.linalg.norm(summed)
            out[f, corner] = summed / norm if norm > 1e-15 else unit[f]
    return out


def build_lod_meshes(variant: int, shape: RockShape) -> List[LodMesh]:
    hard_edge_deg = shape.spec.hard_edge_deg
    ico = trimesh.creation.icosphere(subdivisions=ICO_SUBDIVISIONS)
    directions = np.asarray(ico.vertices, dtype=np.float32)
    radius, _ = shape.clipped_radius(directions)
    dense = (shape.center + directions * radius[:, None]).astype(np.float64)
    dense[:, 1] = np.maximum(dense[:, 1], 0.0)
    dense_faces = np.asarray(ico.faces, dtype=np.int64)

    meshes = []
    for lod, target in LOD_TARGET_TRIS.items():
        mesh = _decimate(dense, dense_faces, target)
        vertices, faces = np.asarray(mesh.vertices), np.asarray(mesh.faces)
        tri = vertices[faces]
        face_normal = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
        face_normal /= np.maximum(np.linalg.norm(face_normal, axis=1, keepdims=True), 1e-15)
        flat_on_ground = np.all(tri[:, :, 1] < BASE_HEIGHT_EPS, axis=1) & (face_normal[:, 1] < BASE_NORMAL_Y)
        meshes.append(LodMesh(variant, lod, vertices, faces,
                              compute_corner_normals(vertices, faces, hard_edge_deg), flat_on_ground))
    return meshes


# ============================================================================= UV / atlas
def _face_tangent_frame(positions: np.ndarray, uv_unity: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    """dP/du et dP/dv par triangle (positions (F,3,3), uv (F,3,2) avec v vers le haut)."""
    e1, e2 = positions[:, 1] - positions[:, 0], positions[:, 2] - positions[:, 0]
    d1, d2 = uv_unity[:, 1] - uv_unity[:, 0], uv_unity[:, 2] - uv_unity[:, 0]
    det = d1[:, 0] * d2[:, 1] - d2[:, 0] * d1[:, 1]
    valid = np.abs(det) > 1e-14
    r = np.where(valid, 1.0 / np.where(valid, det, 1.0), 0.0)[:, None]
    tangent = (e1 * d2[:, 1:2] - e2 * d1[:, 1:2]) * r
    bitangent = (e2 * d1[:, 0:1] - e1 * d2[:, 0:1]) * r
    return tangent, bitangent


def _corner_angles(positions: np.ndarray) -> np.ndarray:
    """Angle (rad) à chacun des 3 coins de chaque triangle : (F, 3)."""
    angles = np.zeros((len(positions), 3))
    for c in range(3):
        a = positions[:, (c + 1) % 3] - positions[:, c]
        b = positions[:, (c + 2) % 3] - positions[:, c]
        cos = np.sum(a * b, axis=1) / np.maximum(np.linalg.norm(a, axis=1) * np.linalg.norm(b, axis=1), 1e-15)
        angles[:, c] = np.arccos(np.clip(cos, -1.0, 1.0))
    return angles


def finalize_mesh(m: LodMesh, uv_ids_side: np.ndarray, uv_px: np.ndarray, resolution: int) -> UnwrappedMesh:
    """Assemble les sommets finaux (position, uv, normale) et calcule les tangentes par sommet."""
    face_count = len(m.faces)
    base_uv = np.array([0.5, 0.5])                       # base enterrée : un texel quelconque
    uv_index = np.full((face_count, 3), -1, dtype=np.int64)
    uv_index[~m.is_base] = uv_ids_side

    corner_key = np.concatenate([
        m.faces.reshape(-1, 1), uv_index.reshape(-1, 1),
        np.round(m.corner_normals.reshape(-1, 3) * 1000.0).astype(np.int64)], axis=1)
    _, first, inverse = np.unique(corner_key, axis=0, return_index=True, return_inverse=True)
    faces = np.asarray(inverse).reshape(face_count, 3)

    vertices = m.vertices[m.faces.reshape(-1)[first]]
    normals = m.corner_normals.reshape(-1, 3)[first]
    uv_flat = uv_index.reshape(-1)[first]
    uv = np.where((uv_flat >= 0)[:, None], uv_px[np.maximum(uv_flat, 0)], base_uv[None, :])

    uv_unity = np.stack([uv[:, 0] / resolution, 1.0 - uv[:, 1] / resolution], axis=1)
    positions = vertices[faces]
    face_t, face_b = _face_tangent_frame(positions, uv_unity[faces])
    weights = _corner_angles(positions)

    tangent_sum = np.zeros_like(vertices)
    for c in range(3):
        np.add.at(tangent_sum, faces[:, c], face_t * weights[:, c:c + 1])
    tangents = tangent_sum - normals * np.sum(normals * tangent_sum, axis=1, keepdims=True)
    tangents /= np.maximum(np.linalg.norm(tangents, axis=1, keepdims=True), 1e-15)

    face_normal = np.cross(positions[:, 1] - positions[:, 0], positions[:, 2] - positions[:, 0])
    sign = np.sign(np.sum(np.cross(face_normal, face_t) * face_b, axis=1))
    sign[sign == 0] = 1.0
    return UnwrappedMesh(m.variant, m.lod, vertices, normals, tangents, faces, sign, m.is_base, uv)


def pack_atlas(meshes: List[LodMesh], resolution: int) -> Tuple[List[UnwrappedMesh], Dict]:
    """Déplie tous les maillages (hors base) dans un seul atlas carré <= resolution."""
    chart_options = xatlas.ChartOptions()
    chart_options.max_cost = 2.5
    chart_options.normal_seam_weight = 4.0

    compact = []
    for m in meshes:
        side_faces = m.faces[~m.is_base]
        used, remapped = np.unique(side_faces, return_inverse=True)
        compact.append((used, np.asarray(remapped).reshape(-1, 3)))

    # resolution=0 : xatlas agrandit l'atlas au lieu de créer une 2e page (dont les UV recouvriraient la 1re).
    # La densité de texels est ajustée jusqu'à remplir au mieux une seule page <= resolution.
    best, best_size, texels_per_unit = None, 0, 0.0
    for _ in range(10):
        atlas = xatlas.Atlas()
        for m, (used, faces) in zip(meshes, compact):
            scaled = (m.vertices[used] * LOD_UV_SCALE[m.lod]).astype(np.float32)
            atlas.add_mesh(scaled, faces.astype(np.uint32))
        pack_options = xatlas.PackOptions()
        pack_options.resolution = 0
        pack_options.padding = PADDING_TEXELS
        pack_options.bilinear = True
        pack_options.bruteForce = True
        pack_options.texels_per_unit = texels_per_unit
        atlas.generate(chart_options, pack_options)
        size = max(atlas.width, atlas.height)
        if atlas.atlas_count == 1 and size <= resolution and size > best_size:
            best, best_size = atlas, size
        if best is not None and best_size >= 0.95 * resolution:
            break
        texels_per_unit = atlas.texels_per_unit * (resolution / size) * 0.985
    if best is None:
        raise RuntimeError("impossible de tenir dans l'atlas demandé")
    atlas = best

    width, height = atlas.width, atlas.height
    unwrapped = []
    for index, (m, (used, faces)) in enumerate(zip(meshes, compact)):
        vmapping, indices, uv = atlas.get_mesh(index)
        indices = np.asarray(indices, dtype=np.int64).reshape(-1, 3)
        # xatlas conserve l'ordre des triangles : on le vérifie plutôt que de le supposer.
        expected = m.vertices[used][faces]
        produced = m.vertices[used][np.asarray(vmapping)][indices]
        if not np.allclose(expected, produced):
            raise RuntimeError(f"xatlas a réordonné les triangles du maillage {index}")
        uv_px = np.asarray(uv, dtype=np.float64) * np.array([width, height])
        unwrapped.append(finalize_mesh(m, indices, uv_px, resolution))
    info = {"atlas_w": int(width), "atlas_h": int(height), "atlas_pages": int(atlas.atlas_count), "charts": int(atlas.chart_count),
            "utilization": float(atlas.utilization), "texels_per_unit": float(atlas.texels_per_unit)}
    return unwrapped, info


# ============================================================================= rastérisation UV
def rasterize_atlas(meshes: List[UnwrappedMesh], resolution: int):
    """Retourne par texel couvert : x, y, id du triangle global, poids barycentriques, id du maillage."""
    xs_all, ys_all, tri_all, w_all, mesh_all = [], [], [], [], []
    face_offset = 0
    eps = RASTER_TOLERANCE
    for mesh_id, m in enumerate(meshes):
        uv = m.uv_atlas
        for t, face in enumerate(m.faces):
            if m.is_base[t]:
                continue
            a, b, c = uv[face[0]], uv[face[1]], uv[face[2]]
            det = (b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])
            if abs(det) < 1e-12:
                continue
            x0 = max(int(np.floor(min(a[0], b[0], c[0]))), 0)
            x1 = min(int(np.ceil(max(a[0], b[0], c[0]))), resolution - 1)
            y0 = max(int(np.floor(min(a[1], b[1], c[1]))), 0)
            y1 = min(int(np.ceil(max(a[1], b[1], c[1]))), resolution - 1)
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
            w1 = ((gx - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (gy - a[1])) / det
            w2 = ((b[0] - a[0]) * (gy - a[1]) - (gx - a[0]) * (b[1] - a[1])) / det
            w0 = 1.0 - w1 - w2
            mask = (w0 >= -eps) & (w1 >= -eps) & (w2 >= -eps)
            if not mask.any():
                continue
            weights = np.clip(np.stack([w0[mask], w1[mask], w2[mask]], axis=1), 0.0, None)
            weights /= weights.sum(axis=1, keepdims=True)
            xs_all.append((gx[mask] - 0.5).astype(np.int32))
            ys_all.append((gy[mask] - 0.5).astype(np.int32))
            tri_all.append(np.full(mask.sum(), face_offset + t, dtype=np.int64))
            w_all.append(weights.astype(np.float32))
            mesh_all.append(np.full(mask.sum(), mesh_id, dtype=np.int32))
        face_offset += len(m.faces)
    return (np.concatenate(xs_all), np.concatenate(ys_all), np.concatenate(tri_all),
            np.concatenate(w_all), np.concatenate(mesh_all))


@dataclass
class GlobalFaces:
    """Attributs par coin de triangle (tous maillages concaténés)."""
    positions: np.ndarray   # (T, 3, 3)
    normals: np.ndarray     # (T, 3, 3)
    tangents: np.ndarray    # (T, 3, 3)
    sign: np.ndarray        # (T,)


def build_global_faces(meshes: List[UnwrappedMesh]) -> GlobalFaces:
    return GlobalFaces(
        np.concatenate([m.vertices[m.faces] for m in meshes]),
        np.concatenate([m.normals[m.faces] for m in meshes]),
        np.concatenate([m.tangents[m.faces] for m in meshes]),
        np.concatenate([m.face_sign for m in meshes]))


# ============================================================================= bake
def _normalize(v: np.ndarray) -> np.ndarray:
    return v / np.maximum(np.linalg.norm(v, axis=1, keepdims=True), 1e-12)


def _tangent_plane(d: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    up = np.where(np.abs(d[:, 1:2]) > 0.95, np.float32([1, 0, 0])[None, :], np.float32([0, 1, 0])[None, :])
    t1 = _normalize(np.cross(up, d))
    t2 = np.cross(d, t1)
    return t1.astype(np.float32), t2.astype(np.float32)


def bake_chunk(shape: RockShape, point: np.ndarray, vertex_normal: np.ndarray,
               vertex_tangent: np.ndarray, sign: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    """Retourne (albédo (N,3) [0,1], normale tangent-space (N,3) [-1,1])."""
    center = shape.center
    d = _normalize((point - center).astype(np.float32))
    radius, on_base = shape.clipped_radius(d)
    q = center + d * radius[:, None]

    t1, t2 = _tangent_plane(d)
    q1, _ = shape.surface_points(_normalize(d + NORMAL_EPS * t1))
    q2, _ = shape.surface_points(_normalize(d + NORMAL_EPS * t2))
    hd_normal = _normalize(np.cross(q1 - q, q2 - q))
    flip = np.sum(hd_normal * d, axis=1) < 0
    hd_normal[flip] *= -1.0

    coarse, _ = shape.clipped_radius(d, full_detail=False)
    concavity = np.zeros(len(d), dtype=np.float32)
    for ring_radius, weight in AO_RADII:
        ring_sum = np.zeros(len(d), dtype=np.float32)
        for k in range(AO_SAMPLES):
            angle = 2.0 * np.pi * k / AO_SAMPLES
            sample_dir = _normalize(d + ring_radius * (np.cos(angle) * t1 + np.sin(angle) * t2))
            ring_sum += shape.clipped_radius(sample_dir, full_detail=False)[0]
        concavity += weight * (ring_sum / AO_SAMPLES - coarse)
    concavity[on_base] = 0.0

    albedo = shape.shade(q, hd_normal, concavity, d)

    n = _normalize(vertex_normal)
    t = vertex_tangent - n * np.sum(n * vertex_tangent, axis=1, keepdims=True)
    t = _normalize(np.nan_to_num(t))
    b = np.cross(n, t) * sign[:, None]
    tangent_space = np.stack([np.sum(hd_normal * t, axis=1), np.sum(hd_normal * b, axis=1),
                              np.maximum(np.sum(hd_normal * n, axis=1), 0.08)], axis=1)
    return albedo, _normalize(tangent_space)


def bake_textures(shapes: List[RockShape], meshes: List[UnwrappedMesh], resolution: int):
    xs, ys, tris, weights, mesh_ids = rasterize_atlas(meshes, resolution)
    faces = build_global_faces(meshes)
    print(f"  texels couverts : {len(xs):,} ({100.0 * len(xs) / resolution ** 2:.1f} %)")

    albedo_img = np.zeros((resolution, resolution, 3), dtype=np.float32)
    normal_img = np.zeros((resolution, resolution, 3), dtype=np.float32)
    covered = np.zeros((resolution, resolution), dtype=bool)
    variant_of_mesh = np.array([m.variant for m in meshes])

    for variant, shape in enumerate(shapes):
        selection = np.nonzero(variant_of_mesh[mesh_ids] == variant)[0]
        started = time.time()
        for start in range(0, len(selection), CHUNK):
            idx = selection[start:start + CHUNK]
            tri, w = tris[idx], weights[idx][:, :, None]
            point = np.sum(faces.positions[tri] * w, axis=1)
            vertex_normal = np.sum(faces.normals[tri] * w, axis=1)
            vertex_tangent = np.sum(faces.tangents[tri] * w, axis=1)
            albedo, tangent_normal = bake_chunk(shape, point, vertex_normal, vertex_tangent, faces.sign[tri])
            albedo_img[ys[idx], xs[idx]] = albedo
            normal_img[ys[idx], xs[idx]] = tangent_normal
            covered[ys[idx], xs[idx]] = True
        print(f"  variante {shape.spec.key} : {len(selection):,} texels en {time.time() - started:.1f}s")
    return albedo_img, normal_img, covered, (xs, ys, mesh_ids)


def dilate_to_png(albedo: np.ndarray, normal: np.ndarray, covered: np.ndarray) -> Tuple[Image.Image, Image.Image]:
    """Étend les bords des îlots de DILATE_TEXELS pixels (mips sans liseré) ; le reste = valeur neutre."""
    distance, (iy, ix) = ndimage.distance_transform_edt(~covered, return_indices=True)
    fill = (~covered) & (distance <= DILATE_TEXELS)
    albedo_out = np.where(covered[:, :, None], albedo, albedo[iy, ix])
    normal_out = np.where(covered[:, :, None], normal, normal[iy, ix])
    albedo_u8 = np.clip(albedo_out * 255.0 + 0.5, 0, 255).astype(np.uint8)
    normal_u8 = np.clip((normal_out * 0.5 + 0.5) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    empty = ~(covered | fill)
    albedo_u8[empty] = FLAT_ALBEDO
    normal_u8[empty] = FLAT_NORMAL
    return Image.fromarray(albedo_u8, "RGB"), Image.fromarray(normal_u8, "RGB")


def make_id_map(meshes: List[UnwrappedMesh], resolution: int, texel_data) -> Image.Image:
    """Carte de contrôle : une teinte par maillage (vérifie l'absence de chevauchement)."""
    xs, ys, mesh_ids = texel_data
    rng = np.random.default_rng(5)
    palette = rng.uniform(70, 255, size=(len(meshes), 3)).astype(np.uint8)
    image = np.full((resolution, resolution, 3), 20, dtype=np.uint8)
    image[ys, xs] = palette[mesh_ids]
    return Image.fromarray(image, "RGB")


def count_overlaps(texel_data, resolution: int) -> int:
    """Texels revendiqués par deux maillages différents (doit valoir 0)."""
    xs, ys, mesh_ids = texel_data
    owner = np.full((resolution, resolution), -1, dtype=np.int32)
    owner[ys, xs] = mesh_ids
    return int(np.count_nonzero(owner[ys, xs] != mesh_ids))


# ============================================================================= export
def write_obj(path: str, mtl_name: str, objects: List[Tuple[str, UnwrappedMesh, np.ndarray]]) -> None:
    lines = ["# Kit ScreePile (généré) - unités : mètres, pivot au centre de la base (y = 0)", f"mtllib {mtl_name}"]
    offset = 1
    for name, m, uv_unity in objects:
        lines.append(f"o {name}")
        lines.append(f"usemtl {MATERIAL_NAME}")
        lines.extend(f"v {x:.6f} {y:.6f} {z:.6f}" for x, y, z in m.vertices)
        lines.extend(f"vt {u:.6f} {v:.6f}" for u, v in uv_unity)
        lines.extend(f"vn {x:.5f} {y:.5f} {z:.5f}" for x, y, z in m.normals)
        lines.append("s 1")
        for a, b, c in m.faces + offset:
            lines.append(f"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}")
        offset += len(m.vertices)
    with open(path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")


def write_mtl(path: str) -> None:
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(f"newmtl {MATERIAL_NAME}\nKd 1.000 1.000 1.000\nKs 0.000 0.000 0.000\n"
                     f"map_Kd ../Textures/ScreePile_D.png\nmap_Bump ../Textures/ScreePile_N.png\n")


def to_unity_uv(m: UnwrappedMesh, resolution: int) -> np.ndarray:
    uv = m.uv_atlas / resolution
    return np.stack([uv[:, 0], 1.0 - uv[:, 1]], axis=1)


def validate(m: LodMesh, u: UnwrappedMesh) -> dict:
    tm = trimesh.Trimesh(m.vertices, m.faces, process=False)
    return {"tris": int(len(m.faces)), "verts_in_obj": int(len(u.vertices)),
            "base_tris_excluded_from_atlas": int(m.is_base.sum()),
            "watertight": bool(tm.is_watertight), "winding_consistent": bool(tm.is_winding_consistent),
            "size_m": [round(float(v), 3) for v in (tm.bounds[1] - tm.bounds[0])],
            "ymin": round(float(tm.bounds[0][1]), 4), "volume_m3": round(float(tm.volume), 4)}


# ============================================================================= main
def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("out_dir")
    parser.add_argument("--res", type=int, default=2048)
    parser.add_argument("--quick", action="store_true", help="atlas 512 pour un test rapide")
    args = parser.parse_args()
    resolution = 512 if args.quick else args.res

    models_dir = os.path.join(args.out_dir, "Models")
    textures_dir = os.path.join(args.out_dir, "Textures")
    work_dir = os.path.join(args.out_dir, "_work")
    for directory in (models_dir, textures_dir, work_dir):
        os.makedirs(directory, exist_ok=True)

    started = time.time()
    shapes = [RockShape(spec) for spec in ROCK_SPECS]

    print("1/5 maillages + LODs")
    lod_meshes: List[LodMesh] = []
    for variant, shape in enumerate(shapes):
        lod_meshes.extend(build_lod_meshes(variant, shape))

    print("2/5 dépliage UV (atlas unique)")
    unwrapped, atlas_info = pack_atlas(lod_meshes, resolution)
    print("  ", atlas_info)

    print("3/5 bake normal + albédo")
    albedo, normal, covered, texel_data = bake_textures(shapes, unwrapped, resolution)
    atlas_info["overlapping_texels"] = count_overlaps(texel_data, resolution)
    print("   texels en chevauchement :", atlas_info["overlapping_texels"])

    print("4/5 textures")
    albedo_image, normal_image = dilate_to_png(albedo, normal, covered)
    albedo_image.save(os.path.join(textures_dir, "ScreePile_D.png"), optimize=True)
    normal_image.save(os.path.join(textures_dir, "ScreePile_N.png"), optimize=True)
    make_id_map(unwrapped, resolution, texel_data).save(os.path.join(work_dir, "atlas_ids.png"))

    print("5/5 export OBJ + rapport")
    write_mtl(os.path.join(models_dir, "ScreePile.mtl"))
    report = {"atlas": atlas_info, "resolution": resolution, "variants": {}}
    for variant, spec in enumerate(ROCK_SPECS):
        objects = []
        report["variants"][spec.key] = {"label": spec.label, "lods": {}}
        for index, m in enumerate(unwrapped):
            if m.variant != variant:
                continue
            uv_unity = to_unity_uv(m, resolution)
            objects.append((f"ScreePile_{spec.key}_LOD{m.lod}", m, uv_unity))
            np.savez_compressed(os.path.join(work_dir, f"mesh_{spec.key}_lod{m.lod}.npz"),
                                V=m.vertices, F=m.faces, N=m.normals, UV=uv_unity)
            report["variants"][spec.key]["lods"][str(m.lod)] = validate(lod_meshes[index], m)
        write_obj(os.path.join(models_dir, f"ScreePile_{spec.key}.obj"), "ScreePile.mtl", objects)

    with open(os.path.join(args.out_dir, "report.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2, ensure_ascii=False)
    print(f"terminé en {time.time() - started:.0f}s -> {args.out_dir}")


if __name__ == "__main__":
    main()
