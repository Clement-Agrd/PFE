# 02 — Le flux d'un tween, étape par étape

---

## Lancer un tween

```
transform.TweenMove(cible, 0.5f).SetEase(Ease.OutBack)
      │
      ├─ from = transform.position   (capturé MAINTENANT)
      ├─ new Tween(transform, 0.5f, t => position = LerpUnclamped(from, cible, t))
      ├─ TweenManager.Play(tween)    → ajouté à la liste du manager
      └─ .SetEase(OutBack)           → configure la courbe, renvoie le tween
```

---

## Chaque frame (TweenManager.Update)

```
pour chaque tween actif :
      done = tween.Tick(dt)
      si done → on le retire de la liste
```

## Dans Tick

```
Tick(dt)
   délai en cours ? → décrémente, sort si pas fini
   _elapsed += dt
   raw = clamp01(_elapsed / duration)      (progression linéaire 0→1)
   valeurEasée = Easing.Evaluate(ease, raw)
   onUpdate(valeurEasée)                    → position = LerpUnclamped(from, to, valeurEasée)
   si raw == 1 → fin (ou boucle suivante)
```

---

## L'easing en image (OutBack)

```
progression linéaire (raw) :  0 ──────────── 1   (le temps, régulier)
                                    │ Easing.Evaluate(OutBack)
valeur easée :                0 ───────╱‾╲── 1   (dépasse 1 puis revient)
```

`OutBack` renvoie une valeur qui monte au-dessus de 1 vers la fin, puis
redescend à 1 → l'objet **dépasse** sa cible puis se recale : le petit « ressort »
final. Sans `LerpUnclamped`, ce dépassement serait coupé.

---

## Exemple chiffré (TweenMove de 0 à 10, OutQuad, 1 s)

```
t=0.0  raw 0.0  easé 0.00  → position 0.0
t=0.25 raw 0.25 easé 0.44  → position 4.4    (déjà loin : décélère à la fin)
t=0.5  raw 0.5  easé 0.75  → position 7.5
t=0.75 raw 0.75 easé 0.94  → position 9.4
t=1.0  raw 1.0  easé 1.00  → position 10.0   → OnComplete()
```

L'objet part vite puis ralentit à l'approche (OutQuad). Un `Linear` donnerait une
vitesse constante ; un `OutBack` dépasserait 10 avant de revenir.

---

## Boucles et pingpong

```
SetLoops(-1, pingpong: true)

boucle 1 : forward, raw 0→1   (aller)
à raw==1 : forward = !forward → backward
boucle 2 : p = 1-raw, raw 0→1  (retour)
... à l'infini
```

Le pingpong inverse le sens à chaque boucle → un va-et-vient. `-1` = infini ;
un nombre positif = ce nombre d'allers(-retours).

---

## Délai et OnComplete

```
SetDelay(0.2f)  → le tween attend 0.2 s avant de commencer à bouger
OnComplete(a)   → 'a' est appelé quand le tween (toutes boucles finies) se termine
```

Chaîner via `OnComplete` permet des séquences :
```csharp
tr.TweenMove(A, 0.5f).OnComplete(() => tr.TweenMove(B, 0.5f));  // A puis B
```

Lis ensuite `03_choix_de_conception.md`.
