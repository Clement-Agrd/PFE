# 02 — Le flux d'une mise à jour, étape par étape

---

## Régler la barre

```
health.OnHealthChanged(80, 100)  →  bar.SetValue(80, 100)
      │
      ├─ _target01 = 80/100 = 0.8
      ├─ label = "80/100"
      └─ la valeur a baissé (avant 1.0) → _catchUpTimer = delayBeforeCatchUp
```

---

## Chaque frame (Update)

```
1. barre principale :
      fill.fillAmount → MoveTowards(0.8, fillSpeed × dt)   ← descend en douceur vers 0.8
      couleur = gradient.Evaluate(fillAmount)              ← se teinte selon le niveau

2. traînée :
      _target01 (0.8) > traînée ?  → NON (on a baissé)
      _catchUpTimer > 0 ? → OUI → on attend (la traînée reste haute)
      ... après le délai → traînée MoveTowards(0.8) doucement
```

---

## L'effet « traînée de dégâts » en image

```
Avant le coup :      [██████████] 100%   (fill + traînée alignés)

Juste après -20 :    [████████░░] fill à 80%
                     [██████████] traînée encore à 100%   ← on VOIT les 20 perdus
                          └── (le segment entre les deux = les dégâts encaissés)

Après le délai :     [████████░░] fill 80%
                     [████████░░] traînée rattrape 80%
```

La barre principale (souvent verte/rouge) chute tout de suite ; la traînée
(souvent blanche) reste, laissant apparaître un segment qui montre exactement les
dégâts subis, puis se résorbe. C'est l'effet des jeux de combat/RPG.

---

## En hausse (soin)

```
bar.SetValue(95, 100)  (on remonte)
      │
      ├─ _target01 = 0.95
      └─ pas de timer (la valeur a augmenté)

Update :
   fill MoveTowards(0.95)          ← monte en douceur
   traînée : 0.95 > traînée ? OUI  → traînée = 0.95 tout de suite
             (pas de « traînée » quand on gagne, seulement quand on perd)
```

---

## World-space, chaque frame

```
WorldSpaceBar.LateUpdate :
   position = ennemi.position + (0, 2, 0)   ← au-dessus de la tête
   forward = caméra.forward                  ← la barre nous fait face
   si barre pleine et hideWhenFull → cachée
```

`LateUpdate` (après le déplacement de l'ennemi) pour que la barre soit toujours
pile au-dessus, sans tremblement.

Lis ensuite `03_choix_de_conception.md`.
