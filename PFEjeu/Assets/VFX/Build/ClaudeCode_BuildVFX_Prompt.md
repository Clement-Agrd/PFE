# Claude Code — Build VFX (fin de construction d'un bâtiment) — standard Piloto Studio

Tu travailles dans un projet Unity **URP** (3e personne). Les fichiers sont dans `Assets/BuildVFX/` :

- `BuildVFX.cs` : `BuildSite` (orchestrateur), `BuildImpact`, `BuildConstructFX`, `BuildCameraShake`, `BuildDemo`
- `Editor/BuildVFXBuilder.cs` : menus `Tools/Build VFX/*` (textures procédurales, matériaux, prefabs, démo)
- `Shaders/BuildReveal.shader` : révélation de bas en haut (hauteur monde), bord incandescent couleur d'équipe, coupe intérieure sombre, ombres
- `Shaders/BuildDust.shader` : bouffée de poussière érodée, ombrée par le haut, soft particles
- `Meshes/` : `BuildFoundation`, `BuildRing`, `BuildDisc`, `Scaffold_Pole`, `Scaffold_Plank`, `Debris_Brick/Stone/Plank/Shard`

## Séquence attendue (bâtiment moyen, type caserne, ~12×9×9 m, `buildTime` 4,5 s)

1. **Chantier (0 → 4,5 s)** : dalle de fondation, échafaudage (poteaux puis planchers, pop-in décalé), poussière au sol, particules incandescentes sur le front de révélation. Le bâtiment apparaît de bas en haut.
2. **Tremblement (0,35 s)** : l'échafaudage vibre, le bâtiment est complet.
3. **Impact** : onde de choc de poussière + anneau couleur d'équipe, explosion de poussière radiale, colonne de poussière, débris et éclats avec collisions, flash de lumière chaude, tache de poussière au sol, secousse caméra selon la distance, l'échafaudage se démonte (éjection, rebond, disparition), léger tassement du bâtiment.

Couleur d'équipe : `BuildSite.teamColor` (HDR) pilote le bord de révélation, le flash et l'anneau. `dustColor` teinte la poussière.

## Adaptation à tous les bâtiments (déjà implémentée, à valider)

- **Matériaux** : `BuildReveal` copie texture de base, couleur, normal map (`_BumpMap`), émissif (`_EmissionMap/_EmissionColor`) et alpha clip (`_Cutoff`) du matériau d'origine. Les surfaces transparentes (Blend) ne sont pas supportées : elles restent opaques pendant l'effet.
- **Emprise orientée** : `BuildSite.orientToBuilding` aligne emprise, fondation, poussière et échafaudage sur la rotation Y. `footprint` : Rectangle, Circle, ou Auto (cercle pour une tour à base carrée).
- **Échafaudage** : nombre d'étages de planchers selon la hauteur (3 à 9), poteaux tous les ~2,2 m sur le périmètre rectangle ou ellipse.
- **LOD** : `forceLod0` fige le LOD 0 pendant l'effet ; tous les LOD reçoivent le matériau de révélation.
- **Presets** : `preset` = Auto (choisi selon la taille), Cabane (<6,5 m), Caserne (défaut), Tour (h > 1,5× largeur et > 10 m), Forteresse (> 22 m), ou Custom (garde les valeurs de l'inspecteur). Chaque preset règle `buildTime`, `tremorTime`, `edgeWidth`, `noiseAmp`, `edgeGlow`, `impactShake`.

Tests à faire en plus : bâtiment tourné de 45°, tour ronde, bâtiment avec fenêtres émissives + normal map, feuillage/toile en alpha clip, prefab avec LODGroup (3 niveaux), cabane 4 m, forteresse 40 m. Aucun artefact visible sur le front de révélation, l'émissif apparaît avec le bâtiment, l'échafaudage ne traverse pas les murs.

## Étape 1 : compilation

1. Importe le dossier, attends la compilation. Corrige toute erreur C#/HLSL (URP 12 à 17 : adapte `FRONT_FACE_TYPE`, `ApplyShadowBias`, `TransformWorldToShadowCoord`, `_MAIN_LIGHT_SHADOWS_SCREEN` selon la version installée).
2. `Tools/Build VFX/Setup URP`, puis `Tools/Build VFX/Build All`, puis `Tools/Build VFX/Create Demo Caserne`.
3. Aucune erreur ni warning dans la console. Vérifie que les prefabs `BuildImpact` et `BuildConstructFX` n'ont aucune référence manquante.

## Étape 2 : fonctionnement

- Play, touche **B** : la séquence se lance et peut être relancée à volonté. **T** change la couleur d'équipe.
- `BuildSite.Play()` ne laisse aucun matériau cloné, objet ou échelle modifiée après la fin (`Cancel()` restaure tout).
- Fonctionne sur un bâtiment réel : teste `Add BuildSite To Selection` sur un prefab avec plusieurs renderers, plusieurs matériaux par renderer et des textures avec tiling.
- Teste 3 tailles (cabane 4 m, caserne 12 m, forteresse 30 m) : poussière, débris, ombre, secousse et échafaudage doivent suivre l'échelle (`radius / 6`).
- Les ombres projetées suivent le clip de révélation (pas d'ombre du bâtiment complet pendant la construction).
- Si la caméra utilise Cinemachine, branche `BuildCameraShake.OnShake` sur un `CinemachineImpulseSource` au lieu du composant.

## Étape 3 : standard Piloto Studio, boucle de critique

Fais jusqu'à **6 itérations**. À chaque itération : capture 4 vues (à 6 m, 15 m, 40 m, contre-plongée à hauteur du joueur) à t = 0,5 s, 2,5 s, 4,5 s (impact), 5,2 s, 7 s ; critique ; corrige ; recommence.

Critères :
- **Lisibilité** : le front de révélation se lit à 40 m ; le moment d'achèvement est évident sans HUD.
- **Poussière** : volumes crédibles, ombrage par le haut, pas de plans durs ni de coupe visible avec le sol (soft particles), pas de répétition visible des bouffées. Densité totale raisonnable (overdraw).
- **Poids** : l'impact doit sembler lourd : temps d'attaque court (<0,15 s), décroissance longue et douce de la poussière (4 à 7 s), onde de choc qui précède la poussière.
- **Débris** : trajectoires variées, rebonds amortis, disparition par réduction de taille, aucun objet qui flotte.
- **Échafaudage** : apparition lisible, démontage physiquement plausible, rien ne traverse le sol.
- **Couleur d'équipe** : visible sur le bord de révélation et l'anneau sans saturer ; test avec 4 équipes (orange, cyan, rouge, vert).
- **Perf** : < 450 particules simultanées en pointe ; aucun GC alloc par frame hors `Play()` ; un seul `BuildImpact` instancié par bâtiment.

Améliorations à envisager si le temps le permet : flipbook de poussière 8×8 généré par script (remplace `T_Dust`), variante pluie/boue (poussière sombre), variante "ruine → rénové", son (marteaux pendant le chantier, grondement + impact), pooling du prefab d'impact.

## Livrable

Un résumé des corrections par itération, la liste des paramètres exposés sur `BuildSite`, et un court GIF/capture avant/après par itération dans `Assets/BuildVFX/Review/`.
