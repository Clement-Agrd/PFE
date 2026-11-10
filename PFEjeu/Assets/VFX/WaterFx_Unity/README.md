# WaterFx — eau réaliste pour Unity URP

Un seul shader (`WaterFx/Water`) pour tous les types d'eau d'environnement : océan, lagon, lac, rivière, marais, flaque.
Six préréglages prêts à l'emploi (prefabs `Water_*`), réglables en jeu via `WaterFxSurface`.

## Prérequis
- Unity 2022 LTS ou Unity 6, **URP** (pas Built-in, pas HDRP).
- Asset URP : cocher **Depth Texture** et **Opaque Texture** (sinon pas de profondeur, de réfraction ni d'écume de rivage).
- HDR conseillé + Bloom (Volume) pour les reflets du soleil.
- Après installation : `Tools > Water FX > 3 Vérifier URP`.

## Installation
1. Copier le dossier `Assets/WaterFx` dans le projet.
2. `Tools > Water FX > 1 Installer` : crée le maillage (grille 128x128), 6 matériaux et 6 prefabs dans `Assets/WaterFx`.
3. `Tools > Water FX > 2 Scène de démo` pour les voir côte à côte.
4. Poser un prefab `Water_*` dans la scène : le plan d'eau est à la hauteur Y du prefab. La taille du prefab = taille du plan (échelle X/Z).

## Préréglages
| Prefab | Taille (m) | Usage | Particularités |
|---|---|---|---|
| Water_Ocean | 200 x 200 | mer ouverte | 4 vagues longues, écume de crêtes, absorption forte du rouge |
| Water_Lagoon | 60 x 60 | eau turquoise peu profonde | fond de sable visible, écume de rivage large |
| Water_Lake | 80 x 80 | lac calme | petites vagues, reflets nets |
| Water_River | 20 x 60 | cours d'eau | courant 0,9 m/s le long de -Z, écume autour des obstacles |
| Water_Swamp | 40 x 40 | marais | turbidité 1,6, vert trouble, reflets atténués |
| Water_Puddle | 6 x 6 | flaque | pas de vagues, réflexion renforcée, bord très net |

Pour un autre usage : dupliquer un matériau et ajuster couleur, absorption, vagues.

## Principaux réglages du matériau
- **Couleur de diffusion + Absorption (R,V,B) + Turbidité** : teinte et clarté (Beer-Lambert). Plus l'absorption est forte, plus l'eau se teinte vite avec la profondeur.
- **Vagues A-D** (dir x, dir z, raideur, longueur d'onde) + amplitude/vitesse globales. Vagues de Gerstner en espace monde (les prefabs adjacents se raccordent).
- **Normales** : 2 couches (A avec courant à deux phases, B défilante) ; force, taille, défilement.
- **Courant** : direction (x,z), vitesse, période de la boucle.
- **Reflets** : force, rugosité, reflet du soleil, distorsion ; réflexion planaire en option.
- **SSS** : lumière à travers les crêtes. **Réfraction**.
- **Écume** : rivage (largeur), crêtes (seuil ; 1 = aucune), quantité.

## Contrôle en jeu
`WaterFxSurface` (sur chaque prefab) : `SetWaveScale`, `SetTurbidity`, `SetFoamAmount`, `SetFlowSpeed` — passent par un MaterialPropertyBlock, le matériau partagé n'est pas modifié (tempête, crue, pollution).

## Réflexion planaire (optionnelle)
Par défaut les reflets viennent de la sonde de réflexion / skybox de la scène (rapide, mais sans reflet des objets proches).
Pour des reflets exacts (arbres, bâtiments) : renommer `Optional~` en `Optional`, ajouter `WaterPlanarReflection` sur le prefab, cocher « Réflexion planaire » sur le matériau. Un rendu de scène supplémentaire par frame.
**Non testé dans Unity** : l'API de caméra secondaire diffère entre Unity 2022 et Unity 6.

## Architecture
```
WaterFx/
├── Shaders/   WaterFxWater.shader + WaterCore.hlsl (maths pures, partagées avec l'aperçu web)
├── Textures/  T_WaterNormal (ripples), T_WaterFoam (écume)  — importées automatiquement
├── Runtime/   WaterFxSurface
├── Editor/    WaterFxMenu (installeur), WaterFxPresets (généré), WaterFxTextureImporter
└── Optional~/ WaterPlanarReflection
```

## Limites connues
- Rien n'a été compilé dans Unity : l'aperçu web valide les maths et l'aspect, pas la compilation HLSL/C#. En cas d'erreur à l'import, copie la Console.
- Plan d'eau plat (pas de rivières en pente, pas de cascades, pas de vagues qui déferlent).
- Pas de caustiques, pas de vue sous l'eau, pas d'interaction (sillages, éclaboussures).
- Écume et ripples procédurales : pour un rendu cinéma, remplacer par des textures photo/simulation (même nom, même format).
- La surface est un plan à 128x128 quads : pour 200 m (Ocean) la résolution est ~1,6 m, suffisante pour des longueurs d'onde >= 8 m. Augmenter `GridResolution` pour plus fin.
- L'eau est transparente (file Transparent) : elle ne reçoit pas les ombres et se trie comme les autres transparents.
