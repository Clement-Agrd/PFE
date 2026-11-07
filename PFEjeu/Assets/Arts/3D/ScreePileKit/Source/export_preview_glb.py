#!/usr/bin/env python3
"""Exporte un GLB d'aperçu (5 variantes x 3 LODs, UN matériau, textures 1024) + les images d'atlas pour la page web.

Usage : python3 export_preview_glb.py <dossier_sortie_du_generateur> <dossier_apercu>
Produit : <apercu>/rocks_preview.glb, rocks_preview.b64.txt (même GLB en base64), atlas_d.jpg, atlas_n.png, atlas_ids.jpg
Dépendances : numpy pillow trimesh
"""
import base64
import io
import os
import sys

import numpy as np
import trimesh
from PIL import Image
from trimesh.visual import TextureVisuals
from trimesh.visual.material import PBRMaterial

KEYS = "AB"
LODS = (0, 1, 2)
PREVIEW_SIZE = 1024


def _reload(image: Image.Image, fmt: str, **options) -> Image.Image:
    buffer = io.BytesIO()
    image.save(buffer, fmt, **options)
    buffer.seek(0)
    return Image.open(buffer)


def main(out_dir: str, preview_dir: str) -> None:
    os.makedirs(preview_dir, exist_ok=True)
    albedo = Image.open(f"{out_dir}/Textures/ScreePile_D.png").convert("RGB").resize((PREVIEW_SIZE,) * 2, Image.LANCZOS)
    normal = Image.open(f"{out_dir}/Textures/ScreePile_N.png").convert("RGB").resize((PREVIEW_SIZE,) * 2, Image.LANCZOS)
    ids = Image.open(f"{out_dir}/_work/atlas_ids.png").convert("RGB").resize((PREVIEW_SIZE,) * 2, Image.NEAREST)

    albedo.save(f"{preview_dir}/atlas_d.jpg", quality=86)
    normal.save(f"{preview_dir}/atlas_n.jpg", quality=90)
    ids.save(f"{preview_dir}/atlas_ids.jpg", quality=86)

    material = PBRMaterial(name="ScreePile", baseColorTexture=_reload(albedo, "JPEG", quality=88),
                           normalTexture=_reload(normal, "PNG", optimize=True),
                           metallicFactor=0.0, roughnessFactor=0.92)
    scene = trimesh.Scene()
    for key in KEYS:
        for lod in LODS:
            data = np.load(f"{out_dir}/_work/mesh_{key}_lod{lod}.npz")
            mesh = trimesh.Trimesh(data["V"], data["F"], vertex_normals=data["N"], process=False)
            mesh.visual = TextureVisuals(uv=data["UV"], material=material)
            name = f"ScreePile_{key}_LOD{lod}"
            scene.add_geometry(mesh, node_name=name, geom_name=name)

    glb_path = f"{preview_dir}/rocks_preview.glb"
    scene.export(glb_path)
    with open(glb_path, "rb") as source, open(f"{preview_dir}/rocks_preview.b64.txt", "w") as target:
        target.write(base64.b64encode(source.read()).decode("ascii"))
    print("GLB", os.path.getsize(glb_path) // 1024, "Ko")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
