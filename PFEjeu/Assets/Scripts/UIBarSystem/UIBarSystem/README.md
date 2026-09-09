# UI Bar System (barres de vie / XP / ressource)

Barres de valeur réutilisables : remplissage animé, traînée de dégâts, couleur
par niveau, HUD ou world-space. La couche de présentation qui affiche toutes tes
données. Namespace : `Core.UIBarSystem`.

## ⚠️ Dépendances
- **UGUI** (`UnityEngine.UI`, présent par défaut) pour les `Image`.
- **TextMeshPro** pour le label optionnel (déjà utilisé par tes autres packages).

## Installation
Dépose le dossier `UIBarSystem/` dans `Assets/`.

## Construire une barre (HUD)
1. Un Canvas. Dedans, un GameObject "HealthBar".
2. Une `Image` de **fond** (rouge sombre), puis une `Image` de **remplissage**
   par-dessus : mets son **Image Type = Filled**, Fill Method = Horizontal.
3. (Option juice) Une `Image` **traînée** ENTRE le fond et le remplissage (blanche
   ou rouge vif), aussi en Filled.
4. Sur "HealthBar" : ajoute `UIBar`. Assigne `fill` (le remplissage) et
   `delayedFill` (la traînée). Optionnel : un `TMP_Text` label, un `Gradient`.

## L'utiliser
```csharp
bar.SetValue(currentHealth, maxHealth);   // règle la barre (0..1 automatique)
bar.SetValue01(0.5f);                     // ou directement en 0→1
bar.SnapToValue();                        // sans animation (init)
```

## 🔗 Brancher sur tes systèmes (le vrai intérêt)
```csharp
// Health :
health.OnHealthChanged += (cur, max) => healthBar.SetValue(cur, max);

// XP :
xp.OnExperienceGained += _ => xpBar.SetValue(xp.CurrentXp, xp.XpToNextLevel);

// Shop / or (label seul) :
wallet.OnBalanceChanged += b => goldBar.SetValue(b, b);  // ou juste un label

// Stats (une ressource type stamina) :
stats.OnStatChanged += def => { if (def == stamina) staminaBar.SetValue(...); };
```
Une ligne par système : toute ta donnée devient visible.

## Barres au-dessus des ennemis (world-space)
1. Fais un prefab "EnemyBar" : un Canvas en **World Space** + un `UIBar`.
2. Ajoute `WorldSpaceBar` : assigne la cible (l'ennemi), l'`offset`, coche
   `billboard` et `hideWhenFull`.
3. À la création de l'ennemi, `SetTarget(enemy.transform)` et branche son Health.

## La traînée de dégâts (juice)
Assigne `delayedFill` : quand la vie chute, la barre principale descend vite, la
traînée reste un instant (`delayBeforeCatchUp`) puis rattrape → on voit un flash
des dégâts encaissés. En soin, la traînée suit immédiatement.

## API
- `UIBar.SetValue(current, max)` / `SetValue01(t)` / `SnapToValue()` / `Normalized`.
- `WorldSpaceBar.SetTarget(transform)`.
- Réglages : `fillSpeed`, `delayedSpeed`, `delayBeforeCatchUp`, `useColorGradient`.

## Fichiers
- `UIBar.cs` — la barre (remplissage + traînée + label + couleur)
- `WorldSpaceBar.cs` — suivi world-space + billboard + masquage
- `Examples/UIBarDemo.cs` — perdre/gagner/plein au clavier
- `Explications/` — documentation détaillée

## Améliore encore avec le Tween
Tu peux remplacer le `MoveTowards` interne par ton `TweenSystem` pour un
remplissage avec easing (ex. `OutCubic`) — dis-moi si tu veux la variante.

## Tester la démo
Construis une barre (fond + fill + traînée). Sur son GameObject : `UIBar`
(assigne les images) + `UIBarDemo`. Lance : 1 = perdre (vois la traînée), 2 =
soigner, 3 = plein.
