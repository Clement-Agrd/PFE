# Layered Sky — ciel en couches, animé et piloté par le gameplay (URP)

Un seul shader skybox + un contrôleur qui change d'**ambiance** avec transition douce
(Jour 16h → Crépuscule → Nuit). Le ciel pilote aussi la lumière, l'ambiance (SH), les réflexions et le brouillard.

```
Gameplay (vagues, boss, hub)
     │  sky.TransitionTo(preset, 8f)   (Jour→Nuit traverse le Crépuscule)
     ▼
SkyController ── SkyState (lerp) ──┬─▶ SkyMaterialApplier     → Skybox/LayeredFantasySky
                                   ├─▶ SkyLightApplier        → Directional Light (soleil / lune)
                                   └─▶ SkyEnvironmentApplier  → ambiance SH + réflexions + fog

Shader (FantasySkyCore.cginc)
 ├── dégradé + lueur d'horizon + sol brumeux
 ├── étoiles + nébuleuse   (1 texture HDR, scintillement procédural)
 ├── aurores               (3 rideaux animés, rayons verticaux)
 ├── étoiles filantes      (procédurales, par préset)
 ├── 1 lune terrestre      (phase = éclairage par le soleil, mers sombres, cratères)
 ├── soleil                (disque HDR + halo)
 ├── rayons de soleil      (crépusculaires, autour du soleil)
 ├── cirrus                (voile haut, dérive)
 └── nuages volumétriques  (raymarch 6 pas + 3 pas lumière, liseré argenté, dérive)
```

## Installation
1. Copier `Assets/LayeredSky` dans votre projet.
2. `Tools ▸ Layered Sky ▸ 2 - Installer dans la scène`
   (crée les 3 presets, le matériau, un `SkyController`, et une Directional Light si absente ; aperçu immédiat).
3. Caméra : **Clear Flags = Skybox**. Lancez Play.

Changer d'ambiance depuis votre code :
```csharp
[SerializeField] private SkyController sky;
[SerializeField] private SkyPreset nightSky;   // SkyPreset_Nuit
void OnWaveStarted() => sky.TransitionTo(nightSky, 8f);   // 8 s ; 0 = immédiat ; -1 = durée par défaut
```
Événements : `sky.PresetChanged` (début), `sky.PresetReached` (fin).

## Créer vos propres ambiances
`Create ▸ Layered Sky ▸ Sky Preset`, ou dupliquez un preset existant. Tout se règle dans l'Inspector
(couleurs HDR, position du soleil/lune, nuages, aurores, lumière, brouillard, exposition).
Les presets par défaut ne sont jamais écrasés par le menu d'installation.

## Ce que le projet doit avoir
- **Linear color space** (Project Settings ▸ Player). Les couleurs sont converties en linéaire à l'envoi au shader.
- **HDR activé** dans l'asset URP et **Bloom** dans un Volume (seuil ≈ 1.0, intensité 0.2–0.4) : sans bloom,
  le soleil et la lune perdent leur éclat.
- Tonemapping dans le Volume (ACES ou Neutral).

## Cycle Jour → Crépuscule → Nuit (v3)
- 3 presets : `SkyPreset_Jour` (16h, plein jour), `SkyPreset_Crepuscule`, `SkyPreset_Nuit`.
- Le SkyController a une **timeline** (Jour, Crépuscule, Nuit). Une transition entre deux ambiances non adjacentes
  (Jour → Nuit au lancement d'une vague) passe par celles du milieu, avec UN seul fondu continu (SmoothStep) :
  le ciel traverse le crépuscule sans à-coup. Timeline vide = fondu direct.
- Soleil : même azimut dans les 3 presets, il descend (36° → 2° → −28°). La lune suit : sa phase dépend de l'angle soleil/lune (comme sur Terre).
- Lune : taille volontairement agrandie (rayon 3.6°, la vraie ≈ 0.26°) pour rester lisible ; réglable dans le preset.
- Aurores boréales : nuit (forte), crépuscule (faible), jour (0). Étoiles filantes : nuit surtout.
- Supprimés par rapport à la v2 : 2e lune, planète à anneaux, preset Assaut (champs `moon2*`, `planet*` retirés de SkyState).

### Mise à jour depuis la v2
Les assets `SkyPreset_*` déjà créés ne sont pas écrasés par le menu 1/2. Faites `Tools ▸ Layered Sky ▸ 3 - Réinitialiser les presets (écrase)`
(les GUID sont conservés) puis `2 - Installer dans la scène` (remplit la timeline si elle est vide). `SkyPreset_Assaut` peut être supprimé.

### Notes
- Le raymarch des nuages est la partie la plus chère. [Guessing] GPU faibles : `CLOUD_STEPS` 6 → 4 dans `FantasySkyCore.cginc`. Non mesuré sur GPU réel.
- [Likely] Le shader est testé via son port GLSL (même code source), pas compilé dans Unity : en cas d'erreur, copier le message Console.

## Coût GPU (shader plein écran)
Les couches coûteuses sont sautées quand leur intensité ≈ 0 (aurores = 0, nuages ≈ 0, lune = 0).
Estimation : les nuages (2 couches × 2 fbm 5 octaves) dominent. Pour alléger : baisser les octaves (`Fbm(..., 5)` → 4)
dans `CloudLayer`, ou mettre `cloudCoverage` à 0 sur mobile. [Guessing] sur les ms exacts : à mesurer avec le Profiler / Frame Debugger.

## Points d'attention (risques)
- Le matériau asset n'est jamais modifié en Play (instance). `PreviewInEditor` (menu contextuel du SkyController) écrit dans
  le matériau asset ET la Directional Light de la scène — c'est voulu, annulable (Ctrl+Z pour la Light).
- Aucun Prefab / ScriptableObject / Animation existant n'est touché. Seuls changent : `RenderSettings` (skybox, ambiance,
  réflexions, soleil, et le fog SI `driveFog` est coché).
- `SkyController` fixe `RenderSettings.skybox` à son instance : n'utilisez pas en parallèle un autre script qui l'assigne
  (ex. `SkyboxSlowRotation` du kit précédent).
- Les nuages sont éclairés par la **lumière principale** (pas par le soleil) : de nuit, la lumière = lune.
- Fog : si `driveFog` est coché, mode ExponentialSquared. Coloré par preset.
- Unity < 2022.2 : géré par `#if UNITY_2022_2_OR_NEWER` dans le menu.

## Limites connues
- Les nuages sont des motifs 2D projetés (pas du volumétrique) : parfaits au-dessus de l'horizon, plats si on regarde très haut
  en caméra rasante. Ils s'effacent près de l'horizon (voulu : le décor prend le relais).
- Aucune montagne/île dans le ciel : mettez de la **géométrie réelle** (îles flottantes, tour géante) à l'horizon pour la parallaxe.
- Les étoiles/nébuleuse sont statiques (texture) ; seul le scintillement bouge.

