# 03 — Choix de conception (et leurs limites)

---

## OverlapSphere vs Trigger vs Raycast

Trois façons de détecter ce qui est interactif autour du joueur :

- **OverlapSphere (choisi)** : le joueur scanne une sphère autour de lui chaque
  frame. Pas de trigger à poser sur chaque objet, choisit facilement le plus
  proche. Version `NonAlloc` → zéro garbage.
- **Triggers** : chaque interactable (ou le joueur) a un trigger ; on suit
  `OnTriggerEnter/Exit`. Événementiel (pas de scan par frame), mais impose des
  triggers partout et le « plus proche » est moins direct.
- **Raycast / regard** : on interagit avec ce qu'on **vise** (FPS). Plus précis
  pour un jeu à la première personne, mais ignore ce qui est à côté.

L'OverlapSphere est le meilleur compromis pour du « ce qui est près de moi »
(top-down, 3D à la 3e personne). Pour un FPS, un raycast depuis la caméra est
souvent préférable — dis-moi si tu veux cette variante.

---

## Pourquoi choisir le plus proche ?

Quand plusieurs objets sont à portée, il faut bien en désigner un. Le **plus
proche** est le choix le plus intuitif (celui vers lequel on marche). On compare
en **distance²** (`sqrMagnitude`) pour éviter une racine carrée inutile.

**Limite :** « plus proche » n'est pas toujours « celui que je regarde ». Pour
prendre en compte l'orientation, on pondérerait par l'angle de vue (produit
scalaire avec la direction du joueur) — extension simple si besoin.

---

## Pourquoi un UnityEvent sur SimpleInteractable ?

C'est ce qui rend le système **relié à tout sans code**. Un designer branche
« ouvrir la porte », « lancer le dialogue », « rouler le loot » dans l'Inspector.
Le système d'interaction reste totalement ignorant de ces systèmes → découplage
maximal, et itération rapide côté design.

Pour de la logique complexe, on hérite plutôt d'`InteractableBase` et on écrit
`Interact` en code — les deux approches coexistent.

---

## Pourquoi GetComponentInParent ?

L'interactable (le script) et le collider ne sont pas toujours sur le même
GameObject : un PNJ peut avoir son collider sur un enfant « corps » et son script
sur la racine. `GetComponentInParent` remonte pour le trouver. Si tout est sur le
même objet, ça marche aussi.

---

## Ce que le système NE fait PAS (volontairement)

- Pas d'**UI fournie** : le loader expose `OnFocusChanged`, à toi de dessiner le
  prompt (panneau + texte, worldspace ou écran).
- Pas de **prise en compte de l'orientation** (celui qu'on regarde) : plus proche
  seulement. Extension par produit scalaire.
- Pas de **2D** intégré : c'est du `Physics` 3D. Pour la 2D, remplace par
  `Physics2D.OverlapCircleNonAlloc`.
- Pas de **maintien / interaction longue** (rester appuyé pour crocheter) : ici
  c'est un appui ponctuel. Ajoutable via un timer sur la touche.
- Pas d'**occlusion** (interagir à travers un mur) : ajoute un raycast de
  visibilité dans `CanInteract` si nécessaire.

---

## Intégration avec les autres packages (via UnityEvent ou code)

| Système | Interaction typique |
|---|---|
| **Loot / Inventory** | coffre → `LootDropper.Drop()` → inventaire |
| **Dialogue** | PNJ → `DialogueRunner.StartDialogue(node)` |
| **Crafting** | établi → ouvrir l'UI de craft |
| **Scene Loader** | porte de zone → `SceneLoader.Instance.LoadScene(...)` |
| **Quest** | remettre un objet → `ReportProgress(...)` |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Interface = cible**   | IInteractable → tout objet interactif, sans le connaître |
| **Détection sans conf** | OverlapSphereNonAlloc (pas de trigger, zéro alloc)    |
| **Câblage sans code**   | SimpleInteractable + UnityEvent                       |
| **Découplage**          | l'Interactor ignore Loot/Dialogue/etc.                |
| **Autonome**            | aucune dépendance à d'autres packages                 |
