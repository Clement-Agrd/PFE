# Easy Terrain — un seul outil pour tout le terrain (Unity 6, URP)

Une fenêtre + un pinceau Scene pour : créer un terrain, générer le relief, peindre les textures (auto ou au pinceau),
**casser la répétition des textures**, disperser arbres et herbe.

```
Assets/EasyTerrain
├── Runtime/EasyTerrainProfile.cs     profil réutilisable (règles de textures / arbres / herbe)
└── Editor/
    ├── EasyTerrainWindow.cs          fenêtre unique (5 onglets)
    ├── EasyTerrainBrush.cs           pinceau Scene (relief, texture, overlay, herbe, arbres)
    ├── TerrainGenerator.cs           presets de relief + lisser / éroder / aplatir
    ├── HeightBrushOps.cs             pinceaux de relief
    ├── SplatOps.cs                   peinture auto + pinceau texture + overlay
    ├── VegetationOps.cs              arbres + herbe (auto et pinceaux)
    ├── TerrainRaycaster.cs           rayon sur la heightmap (sans collider)
    ├── LayerFactory.cs / TerrainFactory.cs / NoiseUtil.cs / BrushSettings.cs
```

## Démarrage (5 minutes)
1. Copier `Assets/EasyTerrain` dans le projet. `Tools > Easy Terrain > Ouvrir`.
2. Bouton **Nouveau** (profil). Onglet **Terrain** : créer le terrain.
3. Onglet **Relief** : choisir un preset (Collines, Montagnes, Plateaux, Île, Dunes, Vallée/rivière) > Générer.
4. Onglet **Textures** : glisser un albedo (+ normale) > « Créer la couche + règle » (répéter : herbe, roche, sable...).
   Régler altitude / pente de chaque règle, puis « Peinture automatique ».
5. Onglet **Végétation** : ajouter des règles d'arbres (prefab) et d'herbe (texture ou prefab) > Disperser.
6. Onglet **Pinceau** + bouton « Pinceau Scene » : retouches à la main. Alt = naviguer.

## Casser la répétition des textures
- **Automatique** : chaque règle de texture a « Break Repetition ». La couche reçoit une *variante* (mêmes textures, tuile à 0,37x,
  décalée) mélangée à la base par taches de bruit. Réglages : taille des taches, couverture, netteté.
- **Texture sur une autre** : mets une AUTRE couche (ex. terre sur herbe) dans `Variant Layer` : elle apparaît en taches sur la base.
- **Au pinceau** : mode « Texture : overlay » = tu choisis la couche à poser, elle n'apparaît qu'en taches (masque de bruit) là où tu peins.
- Coût : une couche de plus par variante (Unity ajoute un passage de rendu tous les 4 couches). Désactive `Break Repetition` sur les couches secondaires.

## Hypothèses / limites (à lire)
- **Non compilé dans Unity** : écrit contre l'API Terrain Unity 6. Si la Console affiche une erreur, copie-la.
- Le matériau du terrain reste le shader URP Terrain Lit standard (aucun shader personnalisé) : robuste, mais la cassure est « baked » dans la splatmap,
  pas un anti-tiling par pixel. Un shader stochastique est possible plus tard (nécessite le TerrainLitPasses.hlsl de ta version d'URP).
- Relancer « Peinture automatique » écrase la peinture manuelle ; relancer « Disperser » remplace les arbres existants. Annuler (Ctrl+Z) fonctionne, mais sur de gros terrains c'est lent (copie complète des données).
- Herbe en prefab (maillage) : `useInstancing` activé ; le matériau du prefab doit supporter le GPU Instancing.
- Pas de travail multi-terrain (tuiles voisines) : un terrain cible à la fois.
- Résolutions conseillées : heightmap 513, alphamap 512 ; 1025/1024 = peinture auto de plusieurs secondes.
