# Prompt à coller dans Claude Code (à la racine du projet Unity)

```
Contexte : projet Unity en URP. Je veux intégrer le kit "CliffPanKit" (dossier fourni) : un pan de falaise modulaire de
4 x 4 m en 3 variantes (A strates, B colonnes, C blocs), à raccorder en largeur/hauteur. Pivot au pied, grille de 4 m,
OBJ avec LOD0/1/2, textures D (albedo+AO) et N (normal map OpenGL) en 2048.

Règles de travail :
- Renvoie toujours les scripts COMPLETS, jamais uniquement les lignes modifiées.
- Conserve tous les commentaires existants, ne supprime aucune fonctionnalité sans me prévenir.
- Ne renomme aucune variable publique/sérialisée sans m'avertir.
- Ne devine jamais : si une info manque (hiérarchie, composants, erreur Console, paramètres Inspector), demande-la avant de proposer une solution.
- Préviens-moi si une modification peut casser des Prefabs, ScriptableObjects, Animations, Events ou références Inspector.
- Priorités : lisibilité, performances, maintenabilité, modularité, SOLID. Explications courtes. Niveau de confiance : [Certain] / [Likely] / [Guessing].

Étapes :
1. Analyse d'abord (sans rien modifier) : version d'Unity, pipeline (confirme URP), organisation actuelle des assets,
   présence d'un système de snap / de grille, LODGroup et paramètres de qualité (LOD Bias).
2. Importe Assets/CliffPan (Models, Textures, Editor). Vérifie qu'aucune erreur de compilation n'apparaît (CliffPanTextureSetup.cs),
   que les textures _N sont bien en Normal Map et les _D en sRGB.
3. Lance Tools > CliffPan > Create URP Materials, puis assigne M_CliffPan_A/B/C aux slots de matériau des OBJ.
4. Vérifie que chaque OBJ a bien produit un LODGroup (LOD0/1/2). Sinon, ajoute-le proprement sur le prefab et propose les seuils de transition.
5. Crée un prefab par variante (CliffPan_A, _B, _C) : pivot au pied, échelle 1, static (Batching Static + Contribute GI),
   collider simple (MeshCollider sur LOD2, ou BoxCollider 4 x 4 x 0,6 m selon mon choix : demande-moi).
6. Crée une scène de test avec les 3 variantes alignées sur une grille de 4 m, plus un second niveau empilé. Contrôle visuellement les raccords :
   aucune fente, aucune rupture d'éclairage au niveau des bords.
7. Résume : fichiers créés/modifiés, prefabs, réglages à ajuster, risques.

Ne fais aucun commit sans que je le demande.
```
