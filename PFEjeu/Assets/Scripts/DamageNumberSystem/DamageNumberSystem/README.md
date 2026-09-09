# Damage Numbers System

Chiffres flottants (floating combat text) au-dessus des ennemis, animés et
recyclés. Namespace : `Core.DamageNumberSystem`.

## ⚠️ Dépendances
- **TextMeshPro** (standard, présent dans la plupart des projets) : le prefab
  utilise un `TMP_Text`.
- **UGUI** : le chiffre est un élément d'UI (RectTransform).
- **PoolingSystem** (`Core.PoolingSystem`) : recyclage des chiffres. Importe-le
  d'abord. (Sans PoolManager assigné, fallback Instantiate/Destroy — mais le
  type `PoolManager` est référencé, donc le package doit être présent.)

## Installation
Dépose le dossier `DamageNumberSystem/` dans `Assets/` (après le PoolingSystem).

## Créer le prefab de chiffre
1. Crée un GameObject UI **TextMeshPro - Text** (UGUI). Style-le (police, taille,
   contour, alignement centré).
2. Ajoute les composants `DamageNumber` (assigne son champ `label` = le TMP) et,
   pour le pooling, `PooledObject`.
3. Fais-en un **prefab**. Supprime-le de la scène.

## Mettre en place le spawner
1. Un **Canvas** en `Screen Space - Overlay`.
2. Sur un GameObject : `DamageNumberSpawner`. Assigne le canvas, le prefab, le
   `PoolManager`, et règle le `worldOffset` (au-dessus de la tête).
3. Fais apparaître un chiffre :
   ```csharp
   spawner.Spawn(enemy.transform.position, 25);              // dégâts entiers
   spawner.Spawn(enemy.transform.position, "CRIT!", Color.red);
   ```

## 🔗 Brancher sur le Health
```csharp
[SerializeField] private DamageNumberSpawner spawner;

private void OnEnable() => GetComponent<Health>().OnDamaged += ShowNumber;
private void OnDisable() => GetComponent<Health>().OnDamaged -= ShowNumber;

private void ShowNumber(DamageInfo info)
{
    Color color = info.Type != null && info.Type.Id == "fire" ? new Color(1f, .5f, 0f) : Color.white;
    spawner.Spawn(transform.position, info.Amount, color);
}
```

## Régler l'animation (sur le prefab)
- `lifetime` — durée avant disparition.
- `floatDistance` — de combien le chiffre monte (px).
- `alphaOverLife` — courbe d'opacité (fondu). Par défaut : plein → transparent.
- `scaleOverLife` — courbe d'échelle (pop/rebond si tu la fais monter puis
  redescendre).
- `horizontalJitter` — dispersion horizontale pour ne pas empiler les chiffres.

## API
- `Spawn(worldPos, int damage)`.
- `Spawn(worldPos, string text)`.
- `Spawn(worldPos, string text, Color color)`.

## Fichiers
- `DamageNumber.cs` — un chiffre (animation + retour au pool)
- `DamageNumberSpawner.cs` — projette monde→écran et sort du pool
- `Examples/DamageNumberDemo.cs` — spawn au clavier
- `Explications/` — documentation détaillée

## Tester la démo
Crée le prefab et le canvas. Sur un GameObject : `DamageNumberDemo` (assigne le
spawner). Lance, appuie sur Espace : un chiffre pop, monte et s'efface (rouge si
« crit »).
