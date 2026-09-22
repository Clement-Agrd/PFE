# 01 — Rôle de chaque script

---

## `Ease.cs` — les courbes disponibles
Un enum : Linear, In/Out/InOut × Quad/Cubic/Sine/Back, OutBounce, OutElastic.
- **In** démarre lent, **Out** finit lent, **InOut** les deux.
- **Back/Elastic** dépassent la cible (ressort) ; **Bounce** rebondit à l'arrivée.

---

## `Easing.cs` — les formules
```csharp
public static float Evaluate(Ease ease, float t)   // t 0→1 → valeur easée
```
Traduit une progression **linéaire** en progression **courbée**. Ex :
```
OutQuad(t) = t × (2 - t)      → décélère à la fin
OutBack(t) = 1 + c3(t-1)³ + c1(t-1)²   → dépasse 1 puis revient (overshoot)
```
Certaines renvoient <0 ou >1 (Back/Elastic) → d'où le LerpUnclamped côté cible.

---

## `Tween.cs` — l'objet d'interpolation
**Rôle :** porter l'état d'une animation et l'avancer.

```csharp
new Tween(target, duration, onUpdate)   // onUpdate reçoit la valeur easée 0→1
```

**Chaînage :**
```csharp
SetEase(ease) · SetDelay(s) · SetLoops(count, pingpong) · SetUnscaledTime(b) · OnComplete(a)
```

**Le tick (appelé par le manager) :**
```csharp
Tick(dt)
   gère le délai
   _elapsed += dt ; raw = clamp01(_elapsed / duration)
   p = forward ? raw : 1-raw           (pour le pingpong)
   onUpdate(Easing.Evaluate(ease, p))
   si raw == 1 : boucle suivante ou fin (OnComplete)
```

`Kill()` stoppe sans OnComplete ; `Complete()` saute à la fin.

---

## `TweenManager.cs` — le moteur
**Rôle :** faire tourner tous les tweens, sans composant sur les cibles.

```csharp
static Tween Play(tween)   // enregistre et renvoie (pour chaîner)
static void Kill(target)   // stoppe les tweens d'une cible

Update : pour chaque tween → Tick(dt) ; retire les terminés
```
Persistant (`DontDestroyOnLoad`), auto-créé au premier usage. Choisit
`deltaTime` ou `unscaledDeltaTime` selon le tween.

---

## `TweenExtensions.cs` — l'API + `Tweener`
**Rôle :** l'ergonomie. Chaque helper capture la valeur de départ, crée le tween
avec le bon `onUpdate`, et le lance.

```csharp
transform.TweenMove(to, d)   → onUpdate: pos = LerpUnclamped(from, to, t)
canvasGroup.TweenFade(a, d)  → onUpdate: alpha = LerpUnclamped(from, a, t)
Tweener.Value(from,to,d,cb)  → onUpdate: cb(LerpUnclamped(from, to, t))
```
`LerpUnclamped` pour laisser passer les overshoots (Back/Elastic).

---

## `Examples/TweenDemo.cs`
1 = déplacement (OutBack) + retour, 2 = scale rebond, 3 = rotation, 4 =
va-et-vient infini. `KillTweens()` d'abord pour éviter les cumuls.

Lis ensuite `02_flux_d_un_tween.md`.
