# Tween System (animation par code)

Mini-lib d'animation chaînable : déplace, scale, tourne, fond, avec ~15 courbes
d'easing. Autonome, style DOTween. Namespace : `Core.TweenSystem`.

## Installation
Dépose le dossier `TweenSystem/` dans `Assets/`. Rien à placer : le manager se
crée tout seul.

## Utilisation
```csharp
using Core.TweenSystem;

transform.TweenMove(target, 0.5f).SetEase(Ease.OutBack);
transform.TweenScale(1.5f, 0.3f).SetEase(Ease.OutBounce);
transform.TweenRotate(target, 0.4f).SetEase(Ease.InOutCubic);
canvasGroup.TweenFade(0f, 0.25f).OnComplete(() => panel.SetActive(false)); // fade out puis cacher
```

## Chaînage
```csharp
transform.TweenMove(pos, 0.6f)
    .SetEase(Ease.OutBack)          // courbe
    .SetDelay(0.2f)                 // attendre avant de démarrer
    .SetLoops(-1, pingpong: true)   // -1 = infini ; pingpong = va-et-vient
    .SetUnscaledTime(true)          // continuer en pause (menus)
    .OnComplete(() => Debug.Log("fini"));
```

## Animer n'importe quelle valeur
```csharp
// Faire monter un score affiché de 0 à 100 en 1 s :
Tweener.Value(0f, 100f, 1f, v => scoreLabel.text = Mathf.RoundToInt(v).ToString());
```

## Cibles fournies
- `Transform` : `TweenMove`, `TweenLocalMove`, `TweenScale` (Vector3 ou float),
  `TweenRotate`.
- `CanvasGroup` : `TweenFade`.
- `SpriteRenderer` : `TweenColor`.
- `Tweener.Value(from, to, duration, onValue)` : une valeur float quelconque.
- `transform.KillTweens()` : stoppe les tweens de cet objet.

## Les courbes (Ease)
`Linear`, `InQuad/OutQuad/InOutQuad`, `InCubic/OutCubic/InOutCubic`,
`InSine/OutSine/InOutSine`, `InBack/OutBack/InOutBack`, `OutBounce`, `OutElastic`.
- **In** : démarre lentement. **Out** : finit lentement. **InOut** : les deux.
- **Back / Elastic** : dépassent la cible (overshoot) → effet ressort/rebond.
- **OutBounce** : rebondit à l'arrivée.

## API
- Extensions ci-dessus → renvoient un `Tween` chaînable.
- `Tween` : `SetEase`, `SetDelay`, `SetLoops(count, pingpong)`, `SetUnscaledTime`,
  `OnComplete`, `Kill()`, `Complete()`.
- `TweenManager.Kill(target)` : stoppe les tweens d'une cible.

## Fichiers
- `Ease.cs` — l'enum des courbes
- `Easing.cs` — les formules d'easing
- `Tween.cs` — l'objet d'interpolation (chaînable)
- `TweenManager.cs` — fait tourner les tweens (persistant, auto-créé)
- `TweenExtensions.cs` — l'API (Transform / CanvasGroup / SpriteRenderer / Value)
- `Examples/TweenDemo.cs` — bouge / rebondit / tourne / va-et-vient
- `Explications/` — documentation détaillée

## Améliore tes autres systèmes
- **Damage Numbers** : anime le pop du chiffre avec `TweenScale`/`TweenMove`.
- **UI** : fondus de menus (`TweenFade`), boutons qui rebondissent (`OutBack`).
- **Scene Loader** : le fondu pourrait passer par un tween.
- **Interaction** : le prompt qui apparaît en douceur.

## Notes
- `CanvasGroup` (UI) et `SpriteRenderer` (2D) sont des types Unity standard.
- Les easings Back/Elastic dépassent [0,1] → on utilise `LerpUnclamped` pour que
  l'overshoot soit visible.
- La valeur de départ est **capturée à l'appel** ; le tween va de là vers la cible.

## Tester la démo
Sur un objet visible : `TweenDemo`. Lance : 1 = déplacement (OutBack), 2 = scale
avec rebond (OutBounce), 3 = rotation, 4 = va-et-vient infini.
