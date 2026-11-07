# CliffPanKit : pan de falaise modulaire (3 variantes)

Paroi rocheuse de **4,00 × 4,00 m**, à raccorder en largeur et en hauteur. Trois variantes qui s'assemblent dans n'importe quel ordre.

| Variante | Forme | Relief max |
|---|---|---|
| A Strates | couches horizontales de calcaire / grès, corniches, fissures par zones | 0,69 m |
| B Colonnes | colonnes verticales de basalte, fentes, joints irréguliers | 0,76 m |
| C Blocs | gros blocs inclinés, surplomb diagonal, fer et mousse sur les replats | 0,95 m |

## Contenu
```
Assets/CliffPan/
├── Models/    CliffPan_A|B|C.obj (+ .mtl)   chaque fichier contient CliffPan_X_LOD0, _LOD1, _LOD2
├── Textures/  CliffPan_X_D.png (albedo + AO, sRGB, 2048)   CliffPan_X_N.png (normal map OpenGL, 2048)
└── Editor/    CliffPanTextureSetup.cs       réglage auto des textures + création des matériaux URP
Preview/       CliffPan_ABC_preview.glb      les 3 variantes (textures 1024), ouvrable dans Blender ou un viewer glTF
Source/        generate_cliff_pan.py, export_preview_glb.py   générateur paramétrique (Python : numpy, scipy, scikit-image, pillow, trimesh)
```

## Caractéristiques
- Unités : mètres, Y vers le haut. Pivot au **pied** du pan, centré en X. Face avant vers +Z, dos plat à −0,60 m.
- Grille de placement : **4 m** en X et en Y.
- Maillage étanche. LOD0 19 584 tris, LOD1 5 184, LOD2 1 440 (≈ 10,5 k / 3 k / 0,9 k sommets).
- UV uniques et sans recouvrement : un seul jeu de textures par variante, une seule texture couvre toute la face avant.
- Raccord : les bords (gauche/droite/haut/bas) suivent la **même courbe périodique** dans les 3 variantes. Écart mesuré aux raccords : 1,5e-7 m en hauteur, 1e-6 sur les normales. La couleur des bords est commune aussi.
- Une bande de 20 cm le long de chaque bord est plus lisse que le reste : elle sert de joint. Pour la masquer, pose un décal de mousse / gravats ou un petit rocher au niveau des raccords.

## Installation dans Unity (URP)
1. Copier `Assets/CliffPan` dans le projet. `CliffPanTextureSetup.cs` règle les textures à l'import (normal map, sRGB, anisotropie).
2. Menu **Tools > CliffPan > Create URP Materials** : crée `M_CliffPan_A/B/C` dans `Assets/CliffPan/Materials`.
3. Sélectionner chaque `.obj` > onglet Materials : assigner `M_CliffPan_X` au slot de matériau (ou glisser le matériau sur l'instance).
4. Les objets nommés `_LOD0/_LOD1/_LOD2` forment normalement un LODGroup automatique à l'import. À vérifier dans l'Inspector.
5. Créer un prefab par variante, puis poser les prefabs avec un snap de 4 m (ProGrids / Snap Settings).

## Régénérer ou créer d'autres variantes
`python3 generate_cliff_pan.py <dossier>` (variable `CLIFF_RES=1024|2048`). Chaque variante est une fonction `variant_x()` qui renvoie un relief : tant que les bords restent pilotés par `FIELD_B`, tout nouveau pan se raccorde aux autres.

## Points d'attention
- Les `.obj` n'embarquent pas de matériaux Unity : l'étape 3 est obligatoire.
- La normal map bake le détail fin (résidu entre le champ de hauteur et le maillage LOD0) : sur LOD1/LOD2 le relief moyen est plus grossier, c'est normal.
- Pas de collider fourni : un `MeshCollider` sur LOD2 convient pour une paroi, ou un `BoxCollider` de 4 × 4 × 0,6 m.
