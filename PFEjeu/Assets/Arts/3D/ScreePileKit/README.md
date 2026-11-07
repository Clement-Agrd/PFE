# Kit « Éboulis » — 2 variantes, budget LOD0 1 500 triangles (URP)

Tas de pierres pour le **pied des falaises**, pensés pour être lus dans une **zone d'éboulis à 25–35 m** :
silhouette marquée, pierres de tailles variées (grosses en bas, petites en haut) sur un monticule de gravier et de terre.
3 LODs chacun (1 500 / 700 / 250 triangles), **un seul atlas 2048² et un seul matériau** pour les deux.

> Hypothèses : « lod : 1 500 » = 1 500 triangles pour le LOD0 ; « +25 à +35 m » = distance de vue de la zone.
> Les seuils du LODGroup sont calculés pour que le LOD0 couvre jusqu'à environ 35 m (voir plus bas).
> Si « +25 à +35 m » désignait autre chose (altitude, largeur de la zone), dis-le-moi : seuls les seuils et la taille changent.

```
Assets/ScreePile
├── Models
│   ├── ScreePile_A.obj, ScreePile_B.obj   (chacun = 3 objets _LOD0 / _LOD1 / _LOD2)
│   └── ScreePile.mtl
├── Textures
│   ├── ScreePile_D.png    albédo sRGB (AO cuite, teinte par pierre, gravier)
│   └── ScreePile_N.png    normal map tangent-space, OpenGL (Y+)
└── Editor
    └── ScreePileSetup.cs  import auto + menu Tools > ScreePile > Create Material And Prefabs
Preview/ScreePile_preview.glb · Source/ (générateur Python) · Docs/ (report.json, atlas_ids.png)
```

## Installation (Unity, URP)

1. Copie `Assets/ScreePile` dans ton projet.
2. Menu **Tools > ScreePile > Create Material And Prefabs** : crée `M_ScreePile` et `ScreePile_A/B.prefab` (LODGroup + MeshCollider).
3. Pose les prefabs au pied de la falaise, enterrés de **10 à 30 cm** : le bord du tas est plat et se fond dans le terrain.
4. Pour remplir une zone de 25–35 m, alterne A et B, change la rotation Y (0–360°) et l'échelle (0,8 à 1,4), et fais-les se chevaucher sur 30–50 %.

## Caractéristiques (mesurées, `Docs/report.json`)

| Variante | Forme | Emprise X × Z × hauteur (m) | Tris LOD0 / 1 / 2 |
|---|---|---|---|
| A | Tas haut et dense, 46 pierres dont 2 gros blocs en pied | 4,12 × 3,52 × 1,07 | 1 500 / 700 / 250 |
| B | Tas bas et étalé, pente douce, 1 grand bloc à un bout | 4,98 × 2,77 × 0,81 | 1 500 / 700 / 250 |

- Maillages étanches, enroulement cohérent, base exactement à `y = 0`, pivot au centre.
- Atlas : 6 maillages, 406 îlots, 0 texel en chevauchement, ≈ 150 texels/m au LOD0 (LOD1 ×0,55, LOD2 ×0,32). La base plate est exclue de l'atlas.
- Normales séparées aux arêtes vives (> 40°) : facettes nettes sur les pierres, normal map faible.
- Mémoire textures : environ 11 Mo compressées avec mips [Likely, BC7 2048²]. Pour 5 Mo de moins, régénère avec `--res 1024` (suffisant à 25–35 m).
- LODGroup (`ScreePileSetup.cs`) : LOD0 au-dessus de 10 % de hauteur d'écran, LOD1 jusqu'à 4 %, LOD2 jusqu'à 1,5 %, masqué sous 0,8 %.
  Avec un tas de ≈ 4,5 m et un FOV vertical de 60° : LOD0 jusqu'à ≈ 35 m, LOD1 jusqu'à ≈ 90 m, LOD2 jusqu'à ≈ 240 m [Likely, dépend de ton FOV].

## Risques à connaître

- **Prefabs** : le menu réécrit `ScreePile_*.prefab` à chaque exécution. Renomme ceux que tu aurais modifiés avant de le relancer ; les GUID sont conservés [Likely].
- **Noms de maillages** : les LODs sont retrouvés par le suffixe `_LOD0/1/2`. En cas d'écart, la Console liste les noms trouvés au lieu de créer un prefab cassé [Likely].
- **MeshCollider** : posé sur le LOD2 (250 triangles), non convexe, pour du décor statique. Si Unity réclame Read/Write, active-le sur les `ScreePile_*.obj` [Guessing].
- **Forme** : le tas est modélisé comme une surface vue de l'extérieur (pas de surplombs ni de cavités sous les pierres). C'est voulu pour le budget, mais ne le mets pas en gameplay où le joueur regarde dessous.
- **Import** : garde *Normals = Import* et *Tangents = Mikk* sur les OBJ, sinon la normal map est faussée.
- **Raccord avec la falaise** : la base est un contour libre sans face arrière. Adosse le tas à la paroi en l'enfouissant partiellement dans le pan de falaise.

## Régénérer ou modifier

```bash
pip install -r Source/requirements.txt
python3 Source/generate_scree.py ./sortie              # atlas 2048, ~2 min
python3 Source/generate_scree.py ./sortie --res 1024   # textures plus légères
python3 Source/generate_scree.py ./sortie --quick      # test (512)
python3 Source/export_preview_glb.py ./sortie ./apercu
```

`Source/scree_shapes.py` > `SCREE_SPECS` : `mound_extents` (taille et hauteur du tas), `stone_count`, `size_range`,
`extra_stones` (gros blocs imposés : x, z, demi-taille), `roughness`, `stone_color`, `moss_amount`.
Triangles par LOD : `LOD_TARGET_TRIS` dans `generate_scree.py`.

## Schéma

```
ScreePile_A (prefab)
├── LOD0 1500 tris  > 10 %  (≤ 35 m) ─┐
├── LOD1  700 tris  > 4 %             ├─ LODGroup
├── LOD2  250 tris  > 1,5 %          ─┘
└── MeshCollider (LOD2)     tous : M_ScreePile → ScreePile_D / ScreePile_N
```
