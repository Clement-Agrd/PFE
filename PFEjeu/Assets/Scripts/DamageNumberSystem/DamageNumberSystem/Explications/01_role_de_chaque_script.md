# 01 — Rôle de chaque script

---

## `DamageNumber.cs` — un chiffre
**Rôle :** animer un chiffre sur sa durée de vie, puis le rendre.

**Réglages (sur le prefab) :**
```csharp
float lifetime;               // durée avant disparition
float floatDistance;          // montée (px)
AnimationCurve alphaOverLife; // fondu (1 → 0)
AnimationCurve scaleOverLife; // pop/rebond éventuel
float horizontalJitter;       // dispersion horizontale
```

**Lancement :**
```csharp
public void Play(Vector2 anchoredPosition, string text, Color color)
{
    // pose la position (+ jitter horizontal), reset le temps,
    // écrit le texte et la couleur, remet l'échelle à 1
}
```

**Animation (Update) :**
```csharp
t = elapsed / lifetime  (0 → 1)
position = départ + haut × (floatDistance × t)     // monte
alpha    = alphaOverLife.Evaluate(t)               // s'efface
scale    = scaleOverLife.Evaluate(t)               // grossit/rebondit
si t == 1 → Despawn()  (retour au pool ou Destroy)
```

Les **courbes** rendent l'animation réglable dans l'Inspector : un fondu simple,
un pop qui grossit puis rétrécit, un rebond... sans écrire de code.

---

## `DamageNumberSpawner.cs` — la fabrique
**Rôle :** placer un chiffre à l'écran à partir d'une position monde.

```csharp
public void Spawn(Vector3 worldPosition, string text, Color color)
{
    // 1) monde → écran
    screenPoint = cam.WorldToScreenPoint(worldPosition + worldOffset)
    si derrière la caméra (z < 0) → on abandonne

    // 2) écran → position ancrée du canvas overlay
    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out anchored)

    // 3) sortir un chiffre (pool ou Instantiate), le parenter au canvas
    // 4) number.Play(anchored, text, color)
}
```

Surcharges pratiques : `Spawn(pos, int damage)` et `Spawn(pos, string text)`.
`worldOffset` place le chiffre au-dessus de la tête plutôt qu'au centre.

---

## `Examples/DamageNumberDemo.cs`
Spawn un chiffre aléatoire à la position de l'objet à l'Espace (rouge si
« crit »). Montre l'appel de base.

Lis ensuite `02_flux_d_un_chiffre.md`.
