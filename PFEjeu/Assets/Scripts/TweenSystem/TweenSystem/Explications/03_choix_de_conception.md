# 03 — Choix de conception (et leurs limites)

---

## Pourquoi un manager central plutôt qu'une coroutine par tween ?

Une coroutine doit tourner sur un MonoBehaviour ; animer un objet sans script (ou
depuis une classe pure) devient pénible. Un manager central :
- fait tourner **tous** les tweens en un seul `Update` ;
- permet de lancer un tween de **n'importe où**, sur **n'importe quoi** ;
- centralise le `Kill(target)`.

C'est l'approche de DOTween et consorts, pour ces raisons.

---

## Pourquoi capturer la valeur de départ à l'appel ?

`transform.TweenMove(to, d)` mémorise `from = position` **au moment de l'appel**.
Le tween va donc de la position actuelle vers la cible. Simple et prévisible.

**Conséquence :** si tu lances deux TweenMove sur le même objet sans tuer le
premier, ils **se battent** (chacun écrit la position). D'où le
`transform.KillTweens()` avant de relancer, dans la démo. Règle : un seul tween à
la fois par propriété d'un objet (ou kill avant de relancer).

---

## Pourquoi LerpUnclamped ?

Les easings **Back** et **Elastic** renvoient volontairement des valeurs hors
[0,1] (le dépassement qui fait le ressort). Un `Lerp` classique bornerait ça et
tuerait l'effet. `LerpUnclamped` laisse la valeur dépasser → l'overshoot est
visible. Pour les easings sans dépassement, ça ne change rien.

---

## Pourquoi un `Tween.Target` (object) ?

Pour pouvoir **tuer par cible** (`Kill(transform)`) sans exposer chaque tween.
Le manager compare `ReferenceEquals(tween.Target, target)`. C'est ce qui permet
`transform.KillTweens()`. Un tween de valeur pure (`Tweener.Value`) a un Target
null (rien à tuer par cible).

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **séquences déclaratives** (une timeline `Append/Join` comme DOTween
  Sequence) : on chaîne via `OnComplete`. Suffit pour la plupart des cas ;
  ajoutable.
- Pas de **from/to explicite** (il part toujours de la valeur actuelle) : pour un
  « from », mets d'abord la valeur de départ puis lance le tween.
- Pas de **easing custom par AnimationCurve** : les courbes sont l'enum `Ease`.
  Ajout facile (une surcharge qui prend une AnimationCurve).
- Pas de **pool de tweens** : chaque tween est un petit objet alloué. Pour des
  milliers de tweens/seconde, un pool réduirait le GC — rarement nécessaire.
- Pas de **cibles avancées** (Material, Light, AudioSource...) : ajoute des
  extensions sur le modèle des existantes.

---

## Intégration avec les autres packages

| Système | Lien |
|---|---|
| **Damage Numbers** | anime le pop/la montée du chiffre par tween au lieu de courbes maison |
| **UI** | fondus (`TweenFade`), boutons rebond (`OutBack`), panneaux qui glissent |
| **Scene Loader** | le fondu de transition peut passer par un tween d'alpha |
| **Interaction** | prompt qui apparaît/disparaît en douceur |
| **Camera Shake** | complémentaire (le shake est du bruit ; le tween, du mouvement dirigé) |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Cœur minimal**        | un tween = progression 0→1 + easing + callback        |
| **Manager central**     | tweens lançables de partout, sans composant           |
| **API expressive**      | extensions chaînables (`.SetEase().OnComplete()`)     |
| **Overshoot correct**   | LerpUnclamped pour Back/Elastic                       |
| **Autonome**            | aucune dépendance ; améliore tous les autres systèmes |
