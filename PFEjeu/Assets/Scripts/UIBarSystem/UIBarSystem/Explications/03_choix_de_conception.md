# 03 — Choix de conception (et leurs limites)

---

## Pourquoi SetValue(current, max) et pas un lien direct vers Health ?

Si la barre connaissait `Health`, elle ne servirait qu'à la vie. En prenant deux
nombres bruts, elle affiche **tout** : PV, XP, mana, chargement, réputation, or...
Chaque système s'y branche en une ligne via son event. C'est ce découplage qui la
rend universelle et réutilisable dans tous tes projets.

Le prix : c'est à toi de faire le pont (`OnHealthChanged += (c,m) => bar.SetValue(c,m)`).
Une ligne, pour une flexibilité totale.

---

## Pourquoi la traînée retardée ?

Un remplissage qui saute est illisible en combat rapide. La traînée (deux barres,
l'une qui chute vite, l'autre qui rattrape) rend **visible** la quantité de
dégâts encaissée d'un coup — l'œil suit. C'est un standard du genre (jeux de
baston, RPG) pour un coût minime (une Image de plus).

**En hausse**, on ne fait pas de traînée (la traînée suit tout de suite) : montrer
un « soin en retard » n'a pas d'intérêt visuel.

---

## Pourquoi MoveTowards et pas un Lerp ?

`MoveTowards(current, target, speed×dt)` avance à **vitesse constante** et
s'arrête pile sur la cible. `Lerp(current, target, speed×dt)` ralentit en
approchant et n'atteint jamais exactement la cible (asymptote). Pour une barre,
MoveTowards est plus prévisible et se termine proprement. (Pour un easing riche,
branche ton TweenSystem.)

---

## Pourquoi Image type = Filled ?

`Image.fillAmount` (0→1) est fait exactement pour ça : remplir une image
proportionnellement, horizontalement ou radialement (barres circulaires !). Pas
besoin de bidouiller la taille d'un RectTransform. Radial = jauge de cooldown,
horizontal = barre de vie.

---

## World-space : billboard et LateUpdate

- **Billboard** (la barre fait face à la caméra) : sinon, vue de côté, elle
  serait illisible. `forward = caméra.forward` la garde plate face à nous.
- **LateUpdate** : on positionne la barre **après** que l'ennemi ait bougé (dans
  Update), sinon elle traînerait d'une frame.

**Limite :** pour une caméra en perspective, `forward = caméra.forward` suffit
en pratique ; pour un rendu parfait tu peux orienter vers la position de la
caméra. Réglable si tu vois un souci d'angle.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **binder automatique** vers Health/XP (pour rester autonome) : tu
  branches l'event toi-même (une ligne). Je peux fournir des composants binder
  prêts si tu veux zéro code.
- Pas de **segments** (barre découpée en crans, style Zelda) : faisable en
  ajoutant des séparateurs par-dessus.
- Pas de **texte flottant de dégâts** : c'est ton package Damage Numbers.
- Pas de **pooling** des barres world-space : pour des centaines d'ennemis, pool
  les barres comme les ennemis.

---

## Intégration avec les autres packages

| Système | Lien |
|---|---|
| **Health** | `OnHealthChanged` → `SetValue(cur, max)` (HUD + barres d'ennemis) |
| **XP** | `OnExperienceGained` → `SetValue(CurrentXp, XpToNextLevel)` |
| **Shop** | `OnBalanceChanged` → label d'or |
| **Stats** | une ressource (stamina) → `SetValue` |
| **Ability** | `CooldownNormalized` → barre radiale (fill circulaire) |
| **Tween** | remplissage avec easing au lieu de MoveTowards |
| **Pooling** | barres d'ennemis recyclées (`SetTarget` à la réutilisation) |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Interface universelle** | SetValue(current, max) → toute source               |
| **Juice**               | remplissage animé + traînée de dégâts                 |
| **Réutilisation**       | même barre en HUD et world-space                      |
| **Découplage**          | la barre ignore Health/XP ; tu branches l'event       |
| **Autonome**            | UGUI + TMP (optionnel) ; aucun autre package requis   |
