# Claude Code — Portail des esprits de la forêt (porte vivante) — standard Piloto Studio

Copie `Unity/SpiritVFX/` dans `Assets/SpiritVFX/` (Unity 6 / URP). Rien n'a été compilé ni testé en moteur : fais-le compiler, tourner, puis polis-le jusqu'à un rendu commercial (référence : packs Piloto Studio « Realistic Environmental Effects »). Ne change pas l'architecture : un shader procédural de surface (le même code que `Spirit Portal.html`), un shader de sol, un shader de distorsion, un composant `SpiritGate`, des particules. Le dossier est indépendant de `PortalVFX` (portail infernal) : les deux peuvent coexister.

## Concept
Une porte vivante, ovale, 6,5 × 10 m, posée dans une clairière de forêt de nuit (`grounded = true`, brouillard teinté vert). Elle fait apparaître des esprits de la forêt. Face avant +Z (côté d'où sortent les esprits), pivot au centre de la base. Vue 3e personne.
1. **Éveil (2 s)** : un réseau de veines de sève lumineuse se propage dans l'air et au sol depuis le centre ; un cadre d'écorce pousse autour de l'ovale en partant du bas, des deux côtés, jusqu'en haut ; une membrane sombre (paupière) apparaît à l'intérieur, parcourue de veines qui pulsent. Spores lumineuses qui montent.
2. **Battement (0,9 s)** : le cadre bat comme un cœur (double battement), la sève afflue, lumière montante.
3. **Ouverture (1,8 s)** : la membrane s'entrouvre par une fente verticale au centre qui s'élargit en ovale (iris organique, léger dépassement), bord doré, éruption de lumière et de spores, gerbe de feuilles, onde de lumière sur le sol.
4. **Stable** : intérieur = forêt des esprits en parallaxe (brume lumineuse, troncs sur 3 plans avec lumière de bord, canopée sombre, rais de lumière, brume au sol, esprits-lucioles qui dérivent avec traînée, spores sur 3 plans). Le cadre respire, la sève circule dans l'écorce, lucioles, esprits qui s'échappent vers le joueur, brume basse, feuilles qui tombent.
5. **Fermeture** : `Close()` referme l'iris, flash doux, la sève se retire.

La parallaxe vient du vecteur vue en espace objet : l'intérieur doit changer de perspective quand la caméra bouge autour du portail.

## Fichiers
- `SpiritGate.cs` : `SpiritGate` (Open / Close / Toggle, `State`, `SpawnPoint`, events `onOpened` / `onClosed`), `SpiritCameraShake`, `SpiritDemo`.
- `Editor/SpiritVFXBuilder.cs` : menus `Tools/Spirit VFX/Setup URP`, `Build All`, `Create Demo`.
- `Shaders/SpiritCore.hlsl` : tout le rendu procédural (identique au GLSL de l'aperçu). `SpiritSurface`, `SpiritGround`, `SpiritDistortion` l'incluent.
- Palette HDR : `hot` = lumière dorée (1.7, 1.5, 0.9), `mid` = sève verte (0.2, 1.15, 0.6), `deep` = ombre de forêt (0.012, 0.06, 0.04). Les couleurs d'écorce et de mousse sont dans le shader.

## Étape 1 : compilation
1. Corrige toute erreur C# / HLSL / shader URP. Points sensibles : `fwidth`, boucles imbriquées dans `forest()`, `acos`, `SampleSceneColor` (DeclareOpaqueTexture), `GetCameraPositionWS`, `TransformWorldToObject`, CBUFFER partagé dans `SpiritCore.hlsl` avec `MaterialPropertyBlock`, `VelocityOverLifetime` (axes dans le même mode), `Volume` / `Bloom` / `Tonemapping` dans le builder (API URP de la version installée), texture `T_Leaf` générée, `leaves.Emit`.
2. `Setup URP` (Depth + **Opaque Texture** + HDR), `Build All`, `Create Demo`. Aucun warning ni erreur. Play, P = ouvrir / fermer.
3. Si l'Opaque Texture est désactivée, la distorsion se coupe seule (warning). Aucun rectangle noir ne doit apparaître.

## Étape 2 : exactitude
- Les 3 quads sont dimensionnés par `Layout()` ; `width` / `height` doivent tout redimensionner (surface, ground, particules, lumières).
- Ordre de rendu : distorsion, sol, surface, particules. Pas de z-fighting (sol à +0,03 m).
- Le cadre d'écorce n'a pas de couture à l'angle ±π (le bruit utilise `cos/sin` de l'angle). Vérifier à gauche de l'ovale.
- Rejouer Open/Close 10 fois, y compris `Close()` pendant `Tearing` : états cohérents, particules arrêtées, lumières éteintes.
- Anti-aliasing du bord (`fwidth`), fondu radial des veines avant le bord du quad, aucune coupure droite.
- Perf : < 1,5 ms GPU en 1080p pour le shader de surface (réduire octaves, boucles des esprits / troncs si besoin, `[unroll]`).

## Étape 3 : polish (boucle critique, 8 itérations)
À chaque itération : 5 captures (éveil, battement, ouverture, stable de face, stable à 45° et 80°), critique, correction.
- **Vivant** : le cadre doit ressembler à de l'écorce et des racines, pas à de la pierre. Ajouter si utile des racines qui rampent hors du cadre vers le sol, des nœuds, de la mousse, des lichens lumineux, des petites fleurs bioluminescentes. La sève doit visiblement circuler.
- **Respiration** : le battement doit se sentir (cadre, lumière, intensité) sans être mécanique ; variations lentes irrégulières.
- **Ouverture** : lecture claire « paupière qui s'entrouvre ». La fente doit avoir une lèvre dorée et des fibres, pas une coupure nette.
- **Forêt intérieure** : vrai sentiment de profondeur. Régler décalage et échelle de chaque plan de troncs, la brume, les rais, la canopée. Ajouter si utile un arbre-gardien immense au fond, des silhouettes d'esprits très lointaines, des fougères en premier plan. L'intérieur doit rester plus sombre que le bord pour que le contraste lise.
- **Esprits** : traînées douces, taille variable, quelques-uns qui franchissent le plan vers le joueur (sprites / particules devant le portail, pas seulement dans le shader).
- **Sol** : veines de sève au sol qui rayonnent, flaque de lumière, onde. L'éclairage dynamique (2 lights du prefab) doit lire sur la mousse et les objets proches ; calibrer à l'exposition de la scène.
- **Bloom** : Volume global Bloom (seuil ~1, intensité ~0,4) + ACES ; pas de zone lessivée blanche. Si l'intérieur paraît laiteux, baisser `fogc`, les rais et les esprits dans `forest()`.
- **Particules** : < 700 actives ; feuilles avec rotation 3D crédible ; lucioles qui clignotent doucement.
- **Variantes** : tester `hot / mid / deep` en bleu lunaire, violet, doré d'automne ; le shader doit rester beau sans changement de code.

## Intégration jeu
- Poser le prefab `SpiritGate` (pivot au sol, +Z vers la zone de jeu). `Open()` au déclenchement ; dans `onOpened`, faire apparaître les esprits à `SpawnPoint`. `Close()` ensuite.
- Caméra : `SpiritCameraShake` sur la caméra principale (réutilise `PortalCameraShake` / `UpgradeCameraShake` / `BuildCameraShake` s'ils existent) ; secousses volontairement faibles (portail vivant, pas explosif).
- Volume global Bloom + ACES (la démo en crée un). Pooling : objet persistant, pas de `Instantiate` pendant la séquence. Multijoueur : visuel local, chaque client appelle `Open()` / `Close()` sur événement serveur.

## Livrables
- Projet qui compile, démo fonctionnelle, prefab `SpiritGate`.
- Court rapport : corrections, valeurs finales, limites (distorsion dépend de l'Opaque Texture ; vue de dos en miroir).
