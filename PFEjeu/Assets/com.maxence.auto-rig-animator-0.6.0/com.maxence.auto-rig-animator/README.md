# Auto Rig Animator (en construction — Phase 4a / 4)

Rigger un mesh brut en cliquant une poignée de repères dessus, puis appliquer des animations
premade quasi instantanément grâce à l'Avatar Humanoid de Unity.

## Où on en est

| Phase | Contenu | Statut |
| --- | --- | --- |
| 1 | Placement des repères sur le mesh | ✅ |
| 2 | Génération du squelette + skinning automatique | ✅ |
| 3 | Génération de l'Avatar Humanoid | ✅ |
| 4a | Lecteur de prévisualisation d'animation (ce package) | ✅ |
| 4b | Bibliothèque de clips + outil de pose/IK pour en créer | ⬜ |

## Utilisation (Phase 1)

1. `Tools > Auto Rig > Place Landmarks`.
2. Glisse ton mesh brut (avec un `MeshFilter`, sans squelette) dans le champ **Mesh brut**.
3. Clique dans la Scene view, à l'endroit indiqué dans la fenêtre, pour chaque repère obligatoire
   (Hips, Spine, Chest, Neck, Head, puis bras et jambes). L'outil passe automatiquement au repère
   suivant après chaque clic.
4. Les repères déjà placés sont des sphères déplaçables (`Handles.FreeMoveHandle`) : glisse-les si
   la position n'est pas bonne.
5. Le bouton **Construire le squelette** s'active une fois tous les repères obligatoires placés.
   Il crée les os, calcule le skinning et transforme le mesh en `SkinnedMeshRenderer` — tout en un
   seul Ctrl+Z.

6. Une fois le squelette construit, le bouton **Générer l'Avatar Humanoid** apparaît. Il reparente
   le mesh et le squelette sous un nouveau GameObject `<nom>_Rig`, construit l'Avatar, le sauvegarde
   dans `Assets/AutoRig/Avatars/` et ajoute un `Animator` sur la racine avec l'Avatar assigné.

## Utilisation (Phase 4a)

1. `Tools > Auto Rig > Preview Animation`.
2. Glisse le `<nom>_Rig` généré en Phase 3 dans **Rig (Animator)**.
3. Glisse n'importe quel `AnimationClip` (Humanoid de préférence) dans **Clip**.
4. **Play** / **Pause** / **Stop**, ou la glissière **Temps** pour scruber manuellement.

## Limitations connues (Phase 4a)

- Un clip Humanoid ne s'échantillonne correctement que si la cible a un `Animator` avec son Avatar
  assigné — glisse le `_Rig`, pas le mesh seul.
- Pas encore de bibliothèque de clips fournie, ni d'outil pour en créer : il faut déjà posséder des
  `AnimationClip` (import FBX, Mixamo, etc.) pour tester ce lecteur.

## Limitations connues (Phase 3)

- Le skinning est un blend linéaire par segment d'os le plus proche, pas une diffusion de chaleur :
  les articulations peuvent paraître un peu facettées de près.
- Le mesh doit être **à peu près en T-pose** au moment du placement des repères (Phase 1) : l'Avatar
  prend la transform locale actuelle de chaque os comme pose de référence.
- La sauvegarde du fichier `.asset` de l'Avatar n'est pas annulable par Ctrl+Z (limitation d'Unity,
  pas du plugin) : annuler après coup laisse le fichier orphelin dans `Assets/AutoRig/Avatars/`.
- Pas encore de bibliothèque d'animations : l'Avatar généré peut déjà recevoir n'importe quel clip
  Humanoid existant (glisse-le dans l'Animator), mais le plugin n'en fournit pas encore.

## Important

- Le mesh doit être posé **à peu près en T-pose** au moment du placement : l'Avatar Humanoid de la
  Phase 3 exige cette pose pour fonctionner correctement.
- Un `MeshCollider` temporaire est ajouté au mesh pour permettre le raycast de placement ; il est
  retiré à la fermeture de la fenêtre si l'objet n'en avait pas déjà un.

## License

MIT, see [LICENSE.md](LICENSE.md).
