# Heal FX — zone de soin lisible (alliés / ennemis)

Un FX qui dit « il y a du soin ici » : disque + anneaux + croix qui tournent au sol, croix et étincelles qui montent,
onde à chaque tick de soin, anneau + gerbe sur chaque cible soignée. Couleurs par camp (vert/or alliés, violet/magenta ennemis).
URP. **Rien n'a été compilé dans Unity** : l'aperçu web teste le même code de sol (HealZoneCore.hlsl), pas les particules.

## Installation (2 minutes)
1. Copie le dossier `Assets/HealFx` dans ton projet (URP).
2. Menu **Tools > Heal FX > 1 Installer** → crée matériaux, palettes, mesh et 4 prefabs dans `Assets/HealFx/Prefabs`.
3. Menu **Tools > Heal FX > 2 Scène de démo** → 2 zones avec unités factices (pulse automatique).

## Utilisation en jeu
```
Zone de soin (sort, tour, totem)
└── prefab HealZone_Ally / HealZone_Enemy   (pivot au sol)
      zone.Pulse();                         // à chaque tick de soin -> onde du centre vers le bord

Unité soignée (allié ou ennemi)
└── prefab HealPulse_Ally / HealPulse_Enemy (enfant, pivot aux pieds)
      target.Pulse();                       // à chaque soin reçu -> anneau + gerbe
```
- `HealZoneFx.Pulsed` (event C#) permet de synchroniser cibles, sons, UI.
- Rayon : `HealZoneFx.SetRadius(r)` (sol + particules suivent). Palette : `SetPalette(...)`.
- Les 2 palettes sont des assets (`Assets/HealFx/Palettes`) : change les couleurs sans toucher aux prefabs.

## Structure
```
HealFx
├── Shaders   HealZoneGround, HealParticle, HealZoneCore.hlsl (maths partagées avec l'aperçu)
├── Runtime   HealZoneFx, HealTargetFx, HealFxPalette, HealDemoRelay
├── Editor    HealFxMenu (installeur), HealFxTextureImporter
├── Textures  T_HealCross, T_HealSparkle, T_HealGlow, T_HealRing
└── Optional~ HealZoneVfxGraphDriver.cs + VFX_HealZone_Recipe.md (version VFX Graph, à construire à la main)
```

## Hypothèses / limites
- Sol plat, pivot du prefab au niveau du sol (le quad est à +0.03 m). Sur terrain en pente : utiliser un Decal URP à la place du quad (non fait).
- Pas de `.vfx` fourni : la recette est dans `Optional~`.
- Les particules passent par un `MaterialPropertyBlock` (intensité) : OK, mais ça désactive le SRP Batcher pour ces renderers.
- Si les shaders sont roses : vérifier URP actif et la Console.
