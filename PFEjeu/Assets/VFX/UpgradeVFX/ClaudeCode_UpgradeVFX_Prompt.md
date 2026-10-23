# Claude Code — Upgrade VFX (amélioration d'un bâtiment, état 1 → état 2) — standard Piloto Studio

Copie `Unity/UpgradeVFX/` dans `Assets/UpgradeVFX/` (Unity 6 / URP). Rien n'a encore été compilé ni testé en moteur : ta mission est de le faire compiler, tourner et le polir jusqu'à un rendu commercial. Ne change pas l'architecture (shaders procéduraux + OBJ + un composant par bâtiment). Le dossier `BuildVFX` est indépendant ; les deux FX peuvent coexister (si `BuildCameraShake` est sur la caméra, `UpgradeCameraShake` le réutilise).

## Concept
Un bâtiment passe d'un état A (ancien modèle) à un état B (nouveau modèle), sans échafaudage :
1. **Charge (0,9 s)** : le bâtiment A se met à vibrer d'énergie (liseré fresnel couleur d'équipe + impulsions montantes), compression légère, particules aspirées en spirale vers le bâtiment, pulsation lumineuse au sol, poussière à la base.
2. **Montée (1,9 s)** : un front d'énergie (bande + anneau + étincelles + lumière) monte de bas en haut. Sous le front, B remplace A ; au front, bord incandescent des deux côtés. Des chevrons montent autour du bâtiment.
3. **Impact** : flash, pilier de lumière, onde de choc, poussière, braises, chevrons, quelques débris, secousse caméra, rebond vertical de B (étirement puis stabilisation). A est désactivé.
4. **Stabilisation (1,4 s)** : le glow s'éteint, B revient à son matériau d'origine.

Couleur d'équipe HDR (`teamColor`), niveau `tier` 1 à 3 (glow, pilier, braises et secousse croissants), échelle automatique selon l'emprise.

## Fichiers
- `UpgradeVFX.cs` : `UpgradeSite` (composant principal), `UpgradeFront`, `UpgradeAura`, `UpgradeBurst`, `UpgradeCameraShake`, `UpgradeDemo`.
- `Editor/UpgradeVFXBuilder.cs` : génère textures, matériaux, prefabs ; menus `Tools/Upgrade VFX/` : Setup URP, Build All, Create Demo Caserne, Add UpgradeSite To Selection.
- `Shaders/UpgradeSwap` (A/B avec front et charge), `UpgradeBeam` (bande, pilier, anneau additifs), `UpgradeDust`.
- `Meshes/` : UpgradeBand, UpgradeFrontRing, UpgradeChevron, UpgradeShock, UpgradeDisc, Debris_*.

## Étape 1 : compilation
1. Importe, corrige toutes les erreurs de compilation et de shader (HLSL URP, API Particle System).
2. `Tools/Upgrade VFX/Setup URP`, puis `Build All`, puis `Create Demo Caserne`. Aucun warning ni erreur console.
3. Points à vérifier en priorité : `velocityOverLifetime` (tous les axes dans le même mode de courbe), `shape.alignToDirection` sur les chevrons (face vers l'extérieur, pointe vers le haut), échelle des shapes `Circle` (ellipse selon l'emprise), mesh renderers de particules avec `Cull Off`, `MaterialPropertyBlock` sur bande/pilier/anneau, `Type.GetType("BuildVFX.BuildCameraShake")`.

## Étape 2 : exactitude fonctionnelle
- Démo : U = lancer, R = retour état 1, 1/2/3 = niveau, T = équipe. Rejouer 10 fois de suite sans fuite de matériaux ni d'objets (`Cancel()` en pleine animation doit remettre A actif, B inactif, matériaux d'origine, LOD libérés).
- Sans `stateB` : le composant fait une simple montée en puissance sur le bâtiment (pas de clip).
- Clip cohérent : A visible au-dessus du front, B en dessous, mêmes noise et même hauteur monde. Aucune zone où ni A ni B ne s'affichent, aucune zone où les deux s'affichent. Ombres (ShadowCaster) et depth cohérentes avec le clip, alpha clip respecté.
- B part inactif : ses renderers sont collectés même inactifs ; ses matériaux d'origine (normal, émissif, alpha clip) sont restaurés à la fin.
- Tester avec : bâtiment tourné de 45°, tour ronde, A et B de hauteurs différentes (B plus haut ou plus bas), A et B avec LODGroup, émissif (fenêtres), feuillage en alpha clip, cabane 4 m, caserne 12 m, forteresse 40 m. Presets Auto OK.
- Pas de GC notable pendant la séquence (hors instanciation des prefabs).

## Étape 3 : polish Piloto Studio (boucle critique, 6 itérations)
À chaque itération : capture 4 images (charge, mi-montée, impact, stabilisation) en vue 3e personne, critique visuelle par rapport à une référence commerciale (pack Realistic Environmental Effects), corrige, recommence.
- **Lisibilité du changement** : le joueur doit comprendre « ça monte de niveau » sans texte. Le front doit être le point focal ; le remplacement A → B doit être net, pas flou.
- **Bord du front** : fin, très chaud (blanc au cœur, couleur d'équipe au bord), irrégularité par bruit, sans aliasing. Intérieur sombre des coupes légèrement teinté.
- **Charge** : liseré fresnel présent mais pas saturé ; impulsions montantes subtiles ; compression d'anticipation à peine visible.
- **Chevrons** : taille proportionnelle au bâtiment, orientés vers l'extérieur, montent en décalé, s'estompent avant le toit.
- **Impact** : flash court (< 0,25 s), pilier qui s'estompe vers le haut avec stries, onde de choc nette au sol, poussière réaliste (ombrée par le haut, érodée), braises qui retombent. Pas de bloom excessif ; valeurs HDR calibrées pour un Bloom URP standard (seuil ~1, intensité ~0,6).
- **Flipbook optionnel** : si la poussière paraît procédurale, générer un flipbook 6×6 de fumée (bruit FBM érodé) et l'utiliser dans `UpgradeDust` ; garder la version actuelle en fallback.
- **Variantes** : vérifier lisibilité avec les 4 couleurs d'équipe (orange, bleu, rouge, vert) et sur sol clair comme sombre.
- **Perf** : < 1 ms CPU par instance ; max ~600 particules actives ; une seule lumière ponctuelle par prefab (flash, front, aura).

## Livrables
- Projet qui compile, démo fonctionnelle, 3 prefabs (`UpgradeAura`, `UpgradeFront`, `UpgradeBurst`).
- Court rapport : ce qui a été corrigé, valeurs finales des presets, limites connues (matériaux Blend/transparents restent opaques pendant l'effet).

## Prérequis projet
- Unity 6 (ou 2022 LTS+) avec URP, rendu Forward ou Forward+. `Setup URP` active Depth Texture et HDR ; ajouter un Volume avec Bloom (seuil ~1, intensité ~0,6) pour que les valeurs HDR de la couleur d'équipe rendent.
- Nouveau ou ancien Input System : `UpgradeDemo` gère les deux (`ENABLE_INPUT_SYSTEM`).
- Ne pas mettre `UpgradeVFX` dans un asmdef sans référencer l'assembly du jeu ; si le projet utilise des asmdef, en créer un seul pour `Assets/UpgradeVFX` (dossier Editor séparé en Editor-only).

## Intégration dans le jeu (à implémenter)
Contexte à demander à l'utilisateur ou à déduire du code existant : comment un bâtiment est représenté (prefab par niveau ? un seul prefab avec enfants par niveau ? changement de mesh/material ?) et où est déclenchée l'amélioration (méthode `Upgrade()`, événement, UI).

1. **Structure d'un bâtiment recommandée** : racine avec `UpgradeSite`, un enfant par niveau (`Level1`, `Level2`, `Level3`), seul le niveau courant actif, tous au même pivot (même position locale, même rotation). Si le jeu remplace plutôt le prefab entier, instancier le nouveau prefab à la même position/rotation, l'assigner à `stateB`, puis détruire l'ancien dans `onCompleted`.
2. **Déclenchement** : à l'achat/validation de l'amélioration, appeler dans cet ordre :
   ```csharp
   site.stateA = currentLevelGO;      // niveau actuel (actif)
   site.stateB = nextLevelGO;         // niveau suivant (inactif, même pivot)
   site.teamColor = team.hdrColor;    // couleur d'équipe HDR (valeurs > 1)
   site.SetTier(nextLevelIndex);      // 1, 2 ou 3 selon le niveau atteint
   site.onCompleted.AddListener(() => building.CommitLevel(nextLevelIndex)); // logique de jeu : stats, état courant
   site.Play();
   ```
   La logique de jeu (stats, coûts, production) ne doit changer qu'à `onCompleted` (ou à `onStarted` si le gameplay doit être immédiat) : pas pendant l'animation, sinon désynchronisation visuelle.
3. **Sécurité** : pendant `IsPlaying`, ignorer une seconde demande d'amélioration ou appeler `Cancel()` puis `ResetToStateA()`. Si le bâtiment est détruit en cours d'effet, `OnDisable` nettoie tout.
4. **Couleur d'équipe** : exposer une conversion `Color → HDR` (multiplier le RGB par ~2 à 2,5 pour obtenir l'intensité attendue). Les valeurs de démo sont orange (2.2, 1.1, 0.25), bleu (0.3, 1.2, 2.6), rouge (2.4, 0.25, 0.3), vert (0.5, 2.2, 0.6).
5. **Caméra** : ajouter `UpgradeCameraShake` sur la caméra principale, sauf si un système de secousse existe déjà ; dans ce cas s'abonner à `UpgradeCameraShake.OnShake(pos, amount, radius)` et relayer vers lui (Cinemachine Impulse par exemple).
6. **Pooling** : les prefabs `UpgradeAura`, `UpgradeFront` et `UpgradeBurst` sont instanciés puis détruits. Si le jeu a un pool, remplacer `Instantiate/Destroy` dans `UpgradeSite.Run`, `UpgradeFront.Finish`, `UpgradeAura.Finish` et `UpgradeBurst.Play` par les appels du pool.
7. **Brouillard de guerre / minimap** : masquer le FX si le bâtiment n'est pas visible par le joueur local (désactiver les renderers de l'aura, du front et du burst, ou ne pas appeler `Play` et basculer directement A → B).
8. **Multijoueur** : le FX est purement visuel et local ; le serveur valide l'amélioration, chaque client appelle `Play()` à la réception de l'événement. Pas de synchronisation de l'animation.
9. **Tests d'intégration** : améliorer 1 → 2 puis 2 → 3 de suite ; annuler en plein milieu ; améliorer deux bâtiments en même temps ; améliorer un bâtiment hors écran ; changer de scène pendant l'effet. Aucune erreur console, aucun objet résiduel (`UpgradeAura`, `UpgradeFront`, `UpgradeBurst`) dans la hiérarchie après 10 s.

## Contenu du dossier à fournir à Claude Code
`UpgradeVFX/` entier (`UpgradeVFX.cs`, `Editor/UpgradeVFXBuilder.cs`, `Shaders/`, `Meshes/`, ce fichier). Copier dans `Assets/UpgradeVFX/`, ouvrir Claude Code à la racine du projet Unity, coller ce document en entier.
