# 01 — Rôle de chaque script

---

## `UIBar.cs` — la barre
**Rôle :** afficher une valeur 0→1 avec de l'animation et du juice.

**Réglages :**
```csharp
Image fill;             // le remplissage (Image type = Filled)
Image delayedFill;      // la traînée retardée (optionnel)
float fillSpeed;        // vitesse de la barre principale
float delayedSpeed;     // vitesse de la traînée
float delayBeforeCatchUp; // temps avant que la traînée rattrape
TMP_Text label;         // "cur/max" (optionnel)
Gradient colorByFill;   // couleur selon le remplissage (optionnel)
```

**Régler la valeur :**
```csharp
public void SetValue(float current, float max)
{
    _target01 = current / max (borné 0..1)
    label.text = "cur/max"
    si la valeur BAISSE → démarre le timer de la traînée   // effet dégâts
}
```

**Animation (Update) :**
```csharp
fill.fillAmount → MoveTowards(_target01, fillSpeed)   // remplissage lisse
couleur = colorByFill.Evaluate(fillAmount)            // vert→rouge

traînée :
   si valeur en hausse (soin) → suit tout de suite
   sinon si timer > 0 → attend (laisse voir les dégâts)
   sinon → rattrape doucement
```

`SnapToValue()` applique tout instantanément (pour l'init), `Normalized` renvoie
la valeur cible.

---

## `WorldSpaceBar.cs` — le suivi world-space
**Rôle :** placer une barre au-dessus d'un objet du monde et la tourner vers la
caméra.

```csharp
LateUpdate :
   position = target.position + worldOffset      // au-dessus de la tête
   si billboard → forward = caméra.forward        // face à la caméra
   si hideWhenFull && barre pleine → SetActive(false)
```

`SetTarget(transform)` rebranche la cible (utile si l'ennemi vient d'un pool).
`hideWhenFull` évite d'afficher une barre pleine (moins de bruit visuel).

---

## `Examples/UIBarDemo.cs`
1 = perdre 15 (vois la traînée), 2 = soigner 10, 3 = plein. Montre l'appel de
base et l'effet de dégâts.

Lis ensuite `02_flux_d_une_maj.md`.
