# 03 — Choix de conception (et leurs limites)

---

## Pourquoi séparer Wallet, Shop et Inventory ?

Chacun a une responsabilité nette :
- **Wallet** : la monnaie (et rien d'autre).
- **Inventory** (ton package) : les objets.
- **Shop** : l'**orchestration** d'une transaction entre les deux.

Le Shop ne stocke pas d'or ni d'objets lui-même : il déplace de la valeur entre
le Wallet et l'Inventory. Ça garde chaque brique réutilisable (un Wallet sert
aussi à un péage, une Inventory à autre chose qu'une boutique).

---

## Pourquoi vérifier tout avant de dépenser ?

Une transaction doit être **atomique du point de vue du joueur** : soit elle
réussit entièrement, soit rien ne change. On teste stock + argent + place
**avant** de modifier quoi que ce soit, puis on ajoute à l'inventaire, et on ne
facture que ce qui est réellement entré. Résultat : jamais de « j'ai payé mais je
n'ai pas l'objet ».

---

## Définition (SO) vs stock runtime : pourquoi ?

Si le stock vivait dans la `ShopDefinition` (l'asset), acheter en Play mode
**modifierait ton asset** de façon permanente (et le stock ne se réinitialiserait
pas d'une partie à l'autre). Le stock restant vit donc dans le composant `Shop`,
reconstruit depuis l'asset à l'`Awake`. L'asset reste le modèle propre.

---

## Le marchand n'achète que ce qu'il liste : pourquoi ?

Pour savoir **combien** payer un objet vendu, il faut un prix. Le shop connaît
les prix des articles qu'il **liste** (via `sellPrice`). Racheter n'importe quel
objet demanderait une **valeur de base** sur chaque `ItemDefinition` (que le
package Inventory n'a pas). C'est une simplification volontaire.

**Pour un rachat universel :** ajoute un champ `baseValue` à l'`ItemDefinition`
(ou une table de prix) et fais le sell price = `baseValue × fraction`. Dis-moi si
tu veux cette extension.

---

## Une seule monnaie : pourquoi, et comment en avoir plusieurs

Le `Wallet` gère un `int`. Simple et suffisant pour la majorité des jeux. Pour
**plusieurs devises** (or, gemmes, jetons) :
- 🥇 un `Wallet` par devise (plusieurs composants), le Shop en référence le bon ;
- 🥈 un Wallet multi-devises (`Dictionary<CurrencyType, int>` + un SO
  `CurrencyType`). Plus flexible, un peu plus lourd.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **prix dynamiques** (offre/demande, marchandage, inflation) : prix fixes
  par article. Ajoutable via un multiplicateur calculé.
- Pas de **rachat universel** (voir plus haut).
- Pas de **taxe / réputation** (remise selon l'affinité) : un multiplicateur sur
  le prix d'achat suffirait.
- Pas d'**UI** : le Shop expose tout ce qu'il faut (articles, prix, stock,
  events), à toi de dessiner l'échoppe.
- Pas de **file / panier** (acheter plusieurs choses d'un coup avant de valider) :
  ici chaque `Buy` est immédiat.

---

## Intégration avec les autres packages

| Système | Lien |
|---|---|
| **Inventory** | socle des transactions (objets entrent/sortent) |
| **Interaction** | marchand → `SimpleInteractable` → ouvrir l'UI de boutique |
| **Loot** | le butin remplit l'inventaire → matière à vendre |
| **Save System** | sauvegarde `Wallet.Balance` (et le stock si voulu) |
| **Quest** | « dépense 100 or » / « vends 10 fourrures » via `OnPurchased`/`OnSold` |
| **Event Bus** | republier les transactions en events globaux |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Single Responsibility** | Wallet (monnaie) ≠ Inventory (objets) ≠ Shop (orchestration) |
| **Transaction atomique** | tout vérifié avant de dépenser ; facturation du réel |
| **Template / instance** | ShopDefinition (SO) vs stock runtime du Shop          |
| **Data-driven**         | articles et prix en asset                             |
| **Découplage / events** | OnPurchased/OnSold/OnTransactionFailed → UI, quêtes    |
