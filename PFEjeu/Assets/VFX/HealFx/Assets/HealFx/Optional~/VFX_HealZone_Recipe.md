# Recette VFX Graph — zone de soin

Je n'ai pas pu générer de fichier `.vfx` fiable (format binaire/graphe propre à Unity, non testable ici).
Le kit fonctionne **déjà** sans VFX Graph (Particle System + shader). Cette recette reproduit la même zone en VFX Graph si tu y tiens.

## Pré-requis
- Package **Visual Effect Graph** installé (URP).
- `Assets > Create > Visual Effects > Visual Effect Graph` → nomme-le `VFX_HealZone`.
- Textures : `Assets/HealFx/Textures/T_HealCross.png`, `T_HealSparkle.png`.

## Propriétés exposées (Blackboard)
| Nom | Type | Défaut |
|---|---|---|
| Radius | Float | 3.4 |
| PrimaryColor | Color | (0.30, 1.00, 0.50) |
| SecondaryColor | Color | (1.00, 0.88, 0.45) |
| Intensity | Float | 1.2 |

## System 1 — Croix qui montent
- **Spawn** : Constant Spawn Rate = `7 * (Radius/3)^2`.
- **Initialize Particle** : Capacity 400 · Lifetime Random 1.6–2.4 · Set Size Random 0.2–0.3 · Set Color = PrimaryColor.
- **Set Position (Shape: Circle/Disc)** : Radius = `Radius * 0.95`, Arc Circle, **Position Mode = Volume**, plan XZ (Rotation −90° X si besoin), Y = 0.05.
- **Update** : Add Velocity (0, Random 0.7–1.3, 0) ; Set Alpha over Life (Composition : 0→1 sur 15 %, 1 jusqu'à 55 %, →0).
- **Output Particle Quad** : Blend **Additive**, texture T_HealCross, Use Alpha = off (masque en R), Color × Intensity, Orient = Face Camera Plane.

## System 2 — Étincelles
Même structure : Spawn Rate = `16 * (Radius/3)^2`, Lifetime 1.2–1.8, Size 0.10–0.17, vitesse Y 0.4–0.9, Color = SecondaryColor, texture T_HealSparkle.

## System 3 — Rafale au tick (événement « OnPulse »)
- **Spawn** : **Single Burst** déclenché par l'événement `OnPulse` (Spawn Context → Start Event = OnPulse), Count 12.
- Position : disque petit (Radius = 0.4), vitesse radiale 1–2 m/s + Y 0.8–1.4, Lifetime 0.8–1.1, Color = PrimaryColor.

## Sol (disque + anneaux)
VFX Graph n'est pas fait pour ça : garde le quad `Ground` du prefab `HealZone_*` (shader HealFx/ZoneGround).
Mets `VFX_HealZone` en enfant du prefab, ajoute `HealZoneVfxGraphDriver` (dossier Optional~), assigne `palette` et `zone`.

## Test
Play → `HealZoneFx.Pulse()` (ou autoPulse) doit déclencher la rafale. Si rien ne sort : l'événement doit s'appeler exactement `OnPulse`.
