# Studio FX — feu, fumée et braises réalistes (URP)

Flipbooks animés + rampe de chaleur HDR + fumée éclairée + distorsion de chaleur. 6 prefabs :

| Prefab | Contenu |
|---|---|
| FX_Torch | flamme (2 couches), braises étirées, fumée éclairée, distorsion de chaleur, lumière |
| FX_Campfire | feu de camp : flamme + coeur, braises, fumée, distorsion, lumière |
| FX_Bonfire | grand brasier (flammes hautes, fumée dense, lumière large) |
| FX_ForgeSparks | gerbe d'étincelles étirées + flash + lueur du métal + filet de fumée |
| FX_ChimneySmoke | fumée de cheminée éclairée, portée par le vent |
| FX_EmberDrift | braises emportées par le vent (zone 6 x 1 x 6 m) |

## Installation
1. Copie `Assets/StudioFx` dans ton projet URP.
2. **Tools > Studio FX > 1 Installer** (matériaux + prefabs dans `Assets/StudioFx`).
3. **Tools > Studio FX > 3 Vérifier URP** : indique ce qu'il faut activer (voir ci-dessous).
4. **Tools > Studio FX > 2 Scène de démo** : les pose en ligne.

## Réglages URP nécessaires (sinon le rendu est dégradé)
- **HDR** activé (asset URP) + **Post Processing** sur la caméra + Volume avec **Bloom** (seuil ~1) : sans bloom, le feu perd son éclat.
- **Depth Texture** : particules « soft » (fondu avec le décor).
- **Opaque Texture** : distorsion de chaleur (couches « HeatHaze »). Sans elle, la couche est simplement invisible.

## Utilisation
Glisse un prefab dans la scène. `StudioFxController` : `SetIntensity(0..2)` (densité + lumières), `SetEmitting(bool)` (arrêt propre).
Les lumières vacillent avec `StudioLightFlicker`.

## Remplacer mes planches par celles d'EmberGen / Houdini (recommandé pour un rendu « studio »)
Les flipbooks livrés sont **procéduraux** (générés par code) : bons, mais en dessous d'une vraie simulation.
Pour passer au niveau supérieur, exporte des planches **8 x 8** (64 images) et écrase les fichiers (même nom, même dossier) :
- `T_StudioFire_8x8.png` : canal **R** = chaleur 0..1 (boucle propre si possible).
- `T_StudioSmoke_8x8.png` : canal **R** = densité, canal **G** = éclairage cuit (1 = côté éclairé), B inutilisé.
- Autre grille (6 x 6, etc.) : change `SheetTiles` dans `Editor/StudioFxMenu.cs` puis relance l'installeur (les shaders lisent le découpage via les UV du Particle System, rien d'autre à changer).
- La rampe `T_StudioRamp_Fire.png` (chaleur -> couleur) se modifie librement : c'est elle qui donne la couleur du feu.

## Structure
```
StudioFx
├── Shaders   StudioFire, StudioSmoke, StudioGlow, StudioHeatHaze, StudioFlipbook.hlsl (commun)
├── Runtime   StudioFxController, StudioLightFlicker
├── Editor    StudioFxMenu (installeur + vérif URP), StudioFxSpec(s) (données), StudioFxTextureImporter
└── Textures  T_StudioFire_8x8, T_StudioSmoke_8x8, T_StudioRamp_Fire, T_StudioSoft
```
Les paramètres (débits, tailles, vitesses, intensités HDR...) sont dans `Editor/StudioFxSpecs.cs`. L'installeur ÉCRASE les prefabs `FX_*`.

## Limites / hypothèses
- **Rien n'a été compilé dans Unity.** L'aperçu web émule les shaders (flipbook, rampe, érosion, fumée éclairée, bloom), pas le moteur de particules d'Unity : bruit, courbes et tri seront un peu différents.
- Le fondu entre images du flipbook repose sur les vertex streams `UV, UV2, AnimBlend` (réglés par l'installeur). Si les flammes « sautent » d'image en image : vérifie ces streams dans le module Renderer de la couche.
- Distorsion de chaleur et particules soft : non simulées dans l'aperçu.
- Unités = mètres, pivot au sol (torche : sommet du manche à ajuster). Si c'est trop petit pour ta caméra de tower defense, agrandis `Size` dans les specs.
- Pluie, neige, lucioles, poussière : pas refaits dans cette version (voir le kit « AmbientFx », stylisé).
