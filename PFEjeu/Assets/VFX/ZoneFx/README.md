# Zone FX — zone de soin (ou de buff / portée) au sol pour Unity 6 + URP

Anneau lumineux fin posé au sol, halo doux, voile intérieur qui tourbillonne, longues voile de lumière au-dessus de l'anneau, étincelles et poussières de lumière.
6 styles prêts (Bleu, Or, Violet, Cyan, Brisé = anneau en tirets, Vert) + couleur libre.

## Installation (2 minutes)
1. Copie le dossier `Assets/ZoneFx` dans ton projet (Unity 6, URP).
2. Menu **Tools > Zone FX > 1 Installer** : crée les matériaux et 2 prefabs dans `Assets/ZoneFx/Prefabs`.
3. **Tools > Zone FX > 3 Vérifier URP** : contrôle Depth Texture (mode Projected) et HDR (bloom).
4. Bloom : un Volume avec l'override Bloom (seuil ~1), Post Processing activé sur la caméra.
5. Test : **Tools > Zone FX > 2 Scène de démo** (6 presets, rangée Flat + rangée Projected).

## Utilisation
Glisse `ZoneFx_Flat` (ou `ZoneFx_Projected`) EN ENFANT de ta tour, position locale (0,0,0), pivot au sol.

```
Tour
└── ZoneFx_Flat          (ZoneFxController)
    ├── Ring             (quad ou cube + matériau d'anneau)
    ├── Veil             (voile de lumière : cylindre ouvert + shader ZoneVeil)
    ├── Sparks           (étincelles)
    └── Motes            (poussières de lumière)
```

```csharp
using ZoneFx;

[SerializeField] private ZoneFxController healZone;

healZone.SetRadius(towerData.HealRadius);   // mètres, monde
healZone.SetPreset(ZoneFxPreset.Green);     // ou healZone.SetColor(Color.magenta)
healZone.Show();  healZone.Hide();          // fondu (Show(true) = immédiat)
healZone.Pulse();                           // rafale d'étincelles : à appeler à chaque tick de soin
```
Le rayon de l'anneau = `SetRadius` exactement (l'anneau est dessiné à 80 % du carré, le reste est le halo).
Le script s'éteint tout seul quand tout est stable : coût nul au repos.

## Flat ou Projected ?
| | Flat | Projected |
|---|---|---|
| Principe | quad horizontal à `groundOffset` (0,05 m) | volume qui relit la profondeur et se projette sur le décor |
| Terrain | plat uniquement | pentes, marches, bosses |
| Coût | minimal | un peu plus (échantillon de profondeur) |
| Besoin | rien | **Depth Texture** activée dans l'asset URP |
| Piège | s'enfonce dans une pente | dessine aussi sur ce qui traverse le volume (personnages, tour) |

Réglages Projected (Inspector) : `projectionHeight` (hauteur du volume, 6 m) et `projectionBelow` (profondeur sous le pivot, 2 m). Ajuste-les à l'amplitude de ton relief.

## Réglages (Inspector de ZoneFxController)
- `radius`, `preset`, `useCustomColor` / `customColor`, `mode` (Flat / Projected)
- `veilIntensity` (0 = pas de voile), `veilHeightPerMeter`, `veilMinHeight`, `veilMaxHeight`
- `intensity` (0–2), `hdrIntensity` (2,4 : force du bloom), `fadeDuration`, `showOnStart`
- `burstSparksPerMeter` : rafale d'étincelles du `Pulse()`
- Boutons de presets dans l'Inspector (marche en édition), Show / Hide / Pulse en Play.

Pour changer l'aspect de l'anneau (épaisseur, halo, voile) : matériaux `M_ZoneRing_Flat` / `M_ZoneRing_Projected` (garde les mêmes valeurs sur les deux). Pour le voile : matériau `M_ZoneVeil` (fondu, colonnes, vitesse de montée). Pour les particules : `ZoneParticleSetup.cs` (débits par mètre, tailles, vitesses) puis relance l'installeur.

## Contenu
- `Runtime/` : ZoneFxController, ZoneParticleSetup, ZoneFxPresets (généré)
- `Editor/` : installeur, inspector, import des textures
- `Shaders/` : ZoneRingFlat, ZoneRingProjected (+ ZoneRingCore.hlsl partagé), ZoneVeil, ZoneParticle
- `Textures/` : 2 masques (étincelle, halo)

## Limites connues
- Code non compilé dans Unity de mon côté : si une erreur apparaît dans la Console, envoie-la moi.
- L'anneau Flat ne suit pas le relief ; le Projected demande la Depth Texture et dessine sur tout ce qui est dans le volume.
- Le voile est un cylindre : vu d'en haut (caméra presque verticale) il s'aplatit autour de l'anneau.
- Après mise à jour du kit, relance **Tools > Zone FX > 1 Installer** pour régénérer les prefabs (le voile y est ajouté).
- L'aperçu web est une approximation (pas de mode Projected).
