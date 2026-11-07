# Prompt Claude Code — intégrer le kit « Éboulis »

Copie-colle le bloc ci-dessous dans Claude Code, à la racine de ton projet Unity.

---

```
Contexte
Je veux intégrer dans mon projet Unity (URP) le kit "ScreePile" : 2 éboulis (tas de pierres) A et B,
3 LODs chacun (1500 / 700 / 250 triangles), un seul atlas de texture et un seul matériau.
Usage : zones d'éboulis au pied des falaises, vues à 25-35 m.
Le kit est dans : <CHEMIN_VERS_LE_KIT>/Assets/ScreePile

Règles de travail (obligatoires)
- Quand tu modifies un script, renvoie-moi le script COMPLET, jamais seulement les lignes modifiées.
- Conserve tous les commentaires existants. Ne supprime aucune fonctionnalité sans me prévenir.
- Ne renomme aucune variable publique sans m'avertir.
- Ne casse jamais les références Unity : préviens-moi si une modification risque d'affecter
  des Prefabs, ScriptableObjects, Animations, Events ou références Inspector.
- Si tu n'es pas sûr, ne devine pas : demande-moi le script, la hiérarchie, les composants,
  l'erreur Console ou les paramètres Inspector.
- Indique ton niveau de confiance : [Certain] / [Likely] / [Guessing].
- Code lisible, modulaire, SerializeField plutôt que public, #region si le script est long,
  pas de Find()/GetComponent répétés dans Update.
- Explications courtes, beaucoup de code.

Étapes
1. Analyse d'abord : version d'Unity, pipeline (vérifie que c'est bien URP), FOV de ma caméra de jeu,
   assets déjà nommés ScreePile. Dis-moi les risques avant de copier quoi que ce soit.
2. Copie Assets/ScreePile dans le projet (Models, Textures, Editor).
3. Laisse Unity importer, vérifie la Console (aucune erreur ni warning lié à ScreePile) et les réglages :
   - ScreePile_D.png : sRGB activé ; ScreePile_N.png : Texture Type = Normal Map ;
   - ScreePile_*.obj : Normals = Import, Tangents = Calculate Mikk T Space.
4. Lance Tools > ScreePile > Create Material And Prefabs. Vérifie Materials/M_ScreePile.mat et
   Prefabs/ScreePile_A.prefab, ScreePile_B.prefab.
5. Vérifie chaque prefab : LODGroup à 3 LODs, un MeshRenderer par LOD, matériau M_ScreePile partout, MeshCollider présent.
6. Calcule, avec le FOV réel de ma caméra, à quelle distance chaque LOD s'affiche (seuils : 10 % / 4 % / 1,5 % de
   hauteur d'écran, taille de l'objet ≈ 4,5 m). Je veux que le LOD0 couvre au moins 35 m : si ce n'est pas le cas,
   propose-moi les nouvelles valeurs et attends ma réponse avant de modifier ScreePileSetup.cs.
7. Crée une scène de test "ScreePileTest" : sol plat, A et B enterrés de 15 cm, une Directional Light, une caméra à 30 m.
   Ne modifie pas mes scènes existantes.
8. Compte-rendu court : ce qui a été créé, warnings éventuels, ce que je dois vérifier à la main.

Si un prefab n'est pas créé (sous-maillages _LOD0/_LOD1/_LOD2 introuvables), affiche-moi la liste des
sous-assets du .obj concerné et attends ma réponse.

Si je demande ensuite un système de remplissage de zone d'éboulis, propose d'abord l'architecture
(ScriptableObject de profil : prefabs, densité, plage d'échelle, enfouissement, graine ; composant de placement
sur une zone / un spline) et la liste des scripts touchés, puis écris les scripts complets.
```
