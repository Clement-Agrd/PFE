# 03 — Choix de conception (et leurs limites)

---

## Pourquoi trois types de modificateur ?

Deux (Flat + un seul type de pourcentage) ne suffisent pas à un vrai
équilibrage : on a besoin de distinguer les pourcentages qui **s'additionnent**
(du même pot) de ceux qui **se composent** (rares et forts). Trois types
couvrent la quasi-totalité des besoins RPG sans surcompliquer.

La formule `(Base + Flat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)` est un
standard éprouvé (on la retrouve dans de nombreux systèmes de stats).

---

## Pourquoi encoder l'ordre dans l'enum ?

`Flat = 100, PercentAdd = 200, PercentMult = 300` : en triant par cette valeur,
les modificateurs se rangent automatiquement dans le bon ordre de calcul. Pas de
logique de tri spéciale à maintenir. Le paramètre `Order` explicite du
`StatModifier` permet en plus d'affiner si un cas particulier l'exige.

---

## Pourquoi la SOURCE sur chaque modificateur ?

C'est ce qui rend le système **utilisable en vrai**. Sans source, retirer « les
bonus de l'épée » obligerait à garder une trace externe des modificateurs posés.
Avec la source, `RemoveAllFromSource(epee)` fait le ménage seul. La source peut
être n'importe quel objet : l'`ItemDefinition`, l'instance de buff, une string...
tant que c'est **la même référence** à l'ajout et au retrait.

**Piège :** si tu passes `new object()` à chaque fois comme source, tu ne
pourras jamais les retrouver. Utilise une source **stable** (l'item lui-même,
un champ mémorisé).

---

## Pourquoi le cache (dirty flag) ?

Les stats sont lues **très souvent** (chaque frame, par l'UI, par le combat).
Recalculer à chaque lecture gâcherait du CPU. Le cache ne recalcule qu'au
changement. C'est invisible à l'usage : tu lis `Value`, tu obtiens la bonne
valeur, sans te soucier de quand elle a été calculée.

---

## Float vs int

Les stats sont en **float** (les pourcentages l'imposent). Pour une stat qui doit
être entière (PV max, niveau), arrondis à la lecture :
`Mathf.RoundToInt(stats.GetValue(maxHealthStat))`. L'arrondi à 4 décimales dans
le calcul évite les `9.999999` disgracieux.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **min/max/clamp** par stat (ex. vitesse jamais négative). Ajoute une
  borne à la lecture, ou un champ min/max sur la `StatDefinition`.
- Pas de **dépendances entre stats** (« PV max = 10 × Vigueur »). Gère ça au-dessus :
  écoute `OnStatChanged(vigueur)` et mets à jour la base de PVMax.
- Pas de **sérialisation** intégrée des modificateurs (les buffs temporaires ne
  se sauvegardent en général pas ; les bonus d'équipement se ré-appliquent au
  chargement depuis l'équipement restauré). La `BaseValue`, elle, se sauvegarde
  facilement.
- Pas de **durée** sur les modificateurs : un buff temporaire, c'est un
  modificateur + un timer qui appelle `RemoveAllFromSource` à la fin → c'est
  exactement le rôle du **Status System** (l'effet ajoute le modif à l'apply, le
  retire à l'expire).

---

## Intégration avec les autres packages

| Système | Lien |
|---|---|
| **Health** | PVMax = une stat ; `OnStatChanged` → `health.SetMaxHealth(value)` |
| **XP / Leveling** | `OnLevelUp` → augmente les `BaseValue` |
| **Status Effects** | un ralenti = un `StatModifier` (source = l'effet) sur Vitesse, retiré à l'expiration |
| **Inventory / Equipment** | équiper = AddModifier(source = item) ; déséquiper = RemoveAllModifiersFromSource(item) |
| **Ability System** | dégâts d'un sort = base × stat d'attaque lue via `GetValue` |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Formule standard**    | Flat / PercentAdd / PercentMult dans le bon ordre     |
| **Retrait par source**  | gestion propre des buffs/équipement                   |
| **Perf (cache)**        | recalcul seulement au changement                      |
| **Data-driven**         | stats identifiées par des SO, valeurs de base réglables|
| **Découplage / events** | OnStatChanged → Health/UI réagissent sans couplage    |
