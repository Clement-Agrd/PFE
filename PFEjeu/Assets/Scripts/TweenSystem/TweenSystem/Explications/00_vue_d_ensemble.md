# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le déroulé d'un tween et les choix de design.

---

## Le problème qu'on résout

Animer par code (« déplace ce panneau ici en 0.3 s avec un petit rebond ») est
partout dans un jeu : menus, UI, juice, portes, caméras. À la main, c'est à
chaque fois une coroutine qui lerp une valeur, avec la même plomberie recopiée.

On veut une **ligne** : `transform.TweenMove(pos, 0.3f).SetEase(Ease.OutBack)`.
Lisible, chaînable, sans écrire de coroutine.

---

## Schéma global

```
   transform.TweenMove(...)         ← API ergonomique (extensions)
        │  crée un
        ▼
   ┌──────────────┐     enregistré dans     ┌────────────────────┐
   │    Tween     │ ──────────────────────► │   TweenManager     │
   │ interpole    │                         │ (persistant)       │
   │ 0→1, easing  │ ◄────── Tick(dt) ────── │ boucle sur tous    │
   └──────┬───────┘                         └────────────────────┘
          │ appelle
          ▼
   ┌──────────────┐
   │    Easing    │  (Linear, Quad, Back, Bounce, Elastic...)
   └──────────────┘
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Un tween = une progression 0→1 + un easing + un callback
Le cœur est minuscule : faire monter t de 0 à 1 sur une durée, le passer dans une
courbe d'easing, et appeler `onUpdate(valeurEasée)`. Tout le reste (déplacer,
scaler, fondre) n'est qu'un `onUpdate` différent.

### 2. Un manager central, pas un composant par cible
Un `TweenManager` persistant fait tourner tous les tweens. Conséquence : on lance
un tween de **n'importe où**, sur **n'importe quoi** (même un objet sans script),
sans ajouter de composant. Comme DOTween.

### 3. API par extensions, chaînable
`transform.TweenMove(...)` se lit comme une méthode native. Chaque appel renvoie
le `Tween` → on enchaîne `.SetEase().SetDelay().OnComplete()`. Une ligne
expressive au lieu d'une coroutine.

---

## En une phrase

> On lance un tween via une extension ; le manager le fait avancer de 0 à 1 en
> appliquant un easing, et le tween met à jour la valeur (position, alpha...)
> chaque frame jusqu'à la fin.

Lis ensuite `01_role_de_chaque_script.md`.
