# 03 — Choix de conception (et leurs limites)

---

## Pourquoi `DamageInfo` (struct riche) plutôt qu'un simple `int` ?

Un `TakeDamage(int)` suffit au début, mais dès qu'on veut des **résistances** ou
créditer un **tueur**, il faut plus d'infos. `DamageInfo` embarque montant +
type + source d'entrée de jeu, sans casser l'API : la surcharge `TakeDamage(int)`
reste là pour les cas simples (elle construit un `DamageInfo` brut).

`readonly struct` → pas d'allocation à chaque coup (important quand ça tape
souvent).

---

## Pourquoi les types de dégâts en ScriptableObject ?

Un `enum DamageType { Fire, Ice }` obligerait à recompiler pour ajouter un type,
et ne se référence pas proprement depuis des assets. Un SO :
- s'ajoute sans code (nouveau type = nouvel asset) ;
- se glisse dans les résistances et les effets par référence ;
- porte un nom d'affichage pour l'UI.

**Piège :** compare les types par **référence d'asset** (`==`). Deux assets « Fire »
distincts sont deux types différents. Garde un seul asset par type.

---

## Pourquoi les i-frames dans le Health ?

Le multi-touch (un objet dans une zone de dégâts touché à chaque frame) est un
bug classique. Une invulnérabilité courte après chaque coup le règle au bon
endroit : dans le Health, pas dispersé dans chaque source de dégâts.

`IsInvulnerable` (manuel) et `_invulnUntil` (i-frames) se cumulent : l'un pour
des états volontaires (dash), l'autre automatique.

---

## Pourquoi Health implémente des interfaces (IDamageable/IHealable) ?

Pour que les systèmes offensifs **ne dépendent pas** de la classe `Health`. Ils
frappent « quelque chose qui est IDamageable » — un ennemi, un mur destructible,
un cristal, un bouclier temporaire qui absorbe avant les PV. Tu peux avoir
plusieurs implémentations d'`IDamageable` sur un même objet (bouclier + PV) en
les chaînant.

---

## Le multiplicateur de résistance : additif ou multiplicatif ?

Ici, **un seul** multiplicateur par type (le premier trouvé). Simple et
prévisible. Si tu veux **empiler** des résistances (armure + buff + terrain),
il faudra les combiner (produit des multiplicateurs, ou soustraction de
pourcentages) — c'est le rôle d'un futur **Stats & Modifiers**, que le Health
consulterait au lieu de sa liste fixe. Dis-moi si tu veux les brancher.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **dégâts sur la durée** intégrés : c'est le rôle du Status System (un
  DoT appelle `TakeDamage` régulièrement).
- Pas de **boucliers/armure** séparés des PV : ajoutables via une seconde couche
  `IDamageable` devant le Health, ou un champ `shield`.
- Pas de **régénération** automatique : un simple `Heal` sur un timer, ou un
  Status « Regen ».
- Pas de **feedback visuel** (flash, knockback) : branche-toi sur `OnDamaged`.
- Pas de **stats dynamiques** (maxHealth qui dépend du niveau) : `SetMaxHealth`
  est là pour ça, à piloter depuis ton XP/Stats.

---

## Intégration avec les autres packages (le "hub")

| Système | Lien |
|---|---|
| **Ability System** | un `DamageEffect` appelle `health.TakeDamage(DamageInfo)` |
| **Status Effects** | un DoT (`OnTick`) appelle `health.TakeDamage(...)` ; un soin → `Heal` |
| **XP / Leveling** | `OnDeath` → `AddExperience` au `DamageInfo.Source` |
| **Save System** | `Capture`/`Restore` des PV |
| **Event Bus** | `OnDeath` → publier `EntityDiedEvent` |
| **Object Pooling** | à `OnDeath`, renvoyer l'ennemi au pool (`PooledObject.Release`) |

Les snippets de câblage sont dans le README.

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Interface = cible**   | IDamageable/IHealable → frappe tout, pas que Health   |
| **Donnée riche**        | DamageInfo (montant + type + source)                  |
| **Data-driven**         | types de dégâts et résistances en assets              |
| **Découplage / events** | OnDamaged/OnDeath → le reste du jeu réagit sans couplage|
| **Perf / zéro GC**      | DamageInfo en readonly struct                         |
