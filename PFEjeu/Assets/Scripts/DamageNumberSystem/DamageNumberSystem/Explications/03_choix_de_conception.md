# 03 — Choix de conception (et leurs limites)

---

## Screen-space overlay vs world-space : pourquoi l'overlay ?

Deux façons d'afficher du texte au-dessus du monde :

- **World-space** : un canvas 3D par chiffre, dans le monde, qui « billboard »
  face à la caméra. Le texte grossit/rétrécit avec la distance (perspective).
- **Screen-space overlay** (choisi ici) : un seul canvas 2D ; on projette la
  position monde à l'écran. Le texte garde **toujours la même taille** à l'écran,
  et un seul canvas gère tous les chiffres.

L'overlay est plus **simple**, plus **performant** (un canvas partagé) et donne
des chiffres lisibles quelle que soit la distance. Le world-space est plus
« intégré » à la scène mais plus lourd. Pour du combat text, l'overlay est le
choix habituel.

**Conséquence :** avec l'overlay, `ScreenPointToLocalPointInRectangle` prend une
caméra `null`. Si tu passes ton canvas en *Screen Space - Camera* ou *World Space*,
il faudra passer la caméra du canvas à la place — dis-le-moi si tu changes de mode.

---

## Pourquoi un prefab plutôt que du texte créé en code ?

Créer un TMP au runtime pose des soucis de police par défaut et empêche de
styliser finement. Un **prefab** te laisse tout régler visuellement (police,
contour, ombre, couleur de base) une fois, et le réutiliser. Le système se
concentre sur l'animation et le placement.

---

## Pourquoi des AnimationCurve pour l'animation ?

Le « feel » d'un chiffre (fondu linéaire ? pop qui rebondit ? qui grossit puis
disparaît ?) est une affaire de **réglage**, pas de code. Les courbes exposent ça
dans l'Inspector : tu dessines la montée d'alpha/scale à la souris. Zéro
recompilation pour tester un ressenti.

---

## Pourquoi le pooling est-il quasi obligatoire ici ?

Un combat peut générer des dizaines de chiffres par seconde. Les
`Instantiate`/`Destroy` répétés créent du garbage → saccades pile pendant
l'action. Le pooling recycle les chiffres → fluide. Le fallback Instantiate
existe pour le prototypage, mais en prod, assigne un PoolManager.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **suivi de cible** : le chiffre part de la position au moment du spawn
  et monte tout seul. Pour qu'il colle à un ennemi mobile, mets à jour sa
  position chaque frame (extension simple).
- Pas de **regroupement** (« 25 + 25 = 50 combiné ») ni de **types visuels**
  intégrés (crit, soin, résisté). Tu passes la couleur/texte ; la logique de
  style t'appartient.
- Pas de **file d'attente / anti-spam** (limiter le nombre à l'écran) : ajoutable
  si tu as des combats très denses.
- Pas de **son** : branche un SFX sur le même event que le spawn.

---

## Intégration avec les autres packages

| Système | Lien |
|---|---|
| **Health** | `OnDamaged(info)` → `spawner.Spawn(pos, info.Amount, couleur)` |
| **Object Pooling** | recyclage des chiffres (déjà utilisé) |
| **Status Effects** | un tick de DoT → un petit chiffre (couleur du type) |
| **Audio** | jouer un SFX de coup au même moment |
| **Stats** | colorer/agrandir le chiffre selon les dégâts (crit = gros et rouge) |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Séparation logique/juice** | le combat ne change pas ; l'affichage écoute OnDamaged |
| **Prefab data-driven**  | style et animation réglés sans code                   |
| **Perf**                | pooling + un seul canvas overlay                      |
| **Projection propre**   | monde → écran → canvas via l'API Unity                |
| **Réglable**            | courbes d'alpha/scale, jitter, offset dans l'Inspector|
