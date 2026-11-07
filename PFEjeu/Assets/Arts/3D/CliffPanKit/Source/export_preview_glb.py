#!/usr/bin/env python3
"""Exporte un GLB d'apercu (LOD0 des 3 variantes, textures 1024) a partir du dossier genere.
Usage : python3 export_preview_glb.py <dossier_sortie_du_generateur> <fichier.glb>   (pip install trimesh pillow numpy)"""
import io
import sys

import numpy as np
import trimesh
from PIL import Image
from trimesh.visual import TextureVisuals
from trimesh.visual.material import PBRMaterial


def as_jpeg(img):
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=88)
    buf.seek(0)
    return Image.open(buf)


def as_png(img):
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    buf.seek(0)
    return Image.open(buf)


def main(out_dir, glb_path):
    scene = trimesh.Scene()
    for k in "ABC":
        mesh_data = np.load(f"{out_dir}/_work/mesh_{k}_lod0.npz")
        albedo = Image.open(f"{out_dir}/Textures/CliffPan_{k}_D.png").convert("RGB").resize((1024, 1024), Image.LANCZOS)
        normal = Image.open(f"{out_dir}/Textures/CliffPan_{k}_N.png").convert("RGB").resize((1024, 1024), Image.LANCZOS)
        material = PBRMaterial(name=f"CliffPan_{k}_Mat", baseColorTexture=as_jpeg(albedo), normalTexture=as_png(normal),
                               metallicFactor=0.0, roughnessFactor=0.92)
        mesh = trimesh.Trimesh(mesh_data["V"], mesh_data["F"], vertex_normals=mesh_data["N"], process=False)
        mesh.visual = TextureVisuals(uv=mesh_data["UV"], material=material)
        scene.add_geometry(mesh, node_name=f"CliffPan_{k}", geom_name=f"CliffPan_{k}")
    scene.export(glb_path)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
