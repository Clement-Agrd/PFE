# Day / Night Cycle System

Cycle jour/nuit : temps qui avance, soleil qui tourne, couleurs/intensité qui
changent, et events aux phases (aube, jour, crépuscule, nuit). Autonome.
Namespace : `Core.DayNightSystem`.

## Installation
Dépose le dossier `DayNightSystem/` dans `Assets/`.

## Mise en place
1. Une **Directional Light** dans la scène (le soleil).
2. Crée un profil : `Assets > Create > Environment > Day Night Profile`. Règle :
   - `sunColor` (Gradient) — bleu nuit → orangé au lever → blanc à midi → orangé
     au coucher → bleu nuit ;
   - `sunIntensity` (Courbe) — 0 la nuit, montée au lever, max à midi ;
   - `ambientColor` (Gradient) — sombre la nuit, clair le jour.
3. Sur un GameObject : ajoute `DayNightCycle`. Assigne le soleil et le profil,
   règle `dayLengthSeconds` (durée réelle d'un cycle) et `startTime01`.

## Réagir au temps
```csharp
cycle.OnPhaseChanged += phase =>
{
    if (phase == DayPhase.Night) enemySpawner.StartWaves();   // les monstres sortent
    if (phase == DayPhase.Dawn)  shop.Open();                 // la boutique ouvre
};

cycle.OnHourChanged += hour => clockUI.text = $"{hour}h00";
cycle.OnDayPassed   += day  => Debug.Log($"Jour {day}");
```

## Contrôler le temps
```csharp
cycle.Paused = true;        // fige le temps
cycle.SetTime(20f);         // saute à 20h (dormir, cinématique...)
float t = cycle.Time01;     // 0 → 1
float h = cycle.Hour;       // 0 → 24
DayPhase p = cycle.Phase;
```

## API
- `Time01`, `Hour`, `DayCount`, `Phase`, `Paused`.
- `SetTime(hour)` / `SetTime01(t)`.
- Events : `OnHourChanged(int)`, `OnPhaseChanged(DayPhase)`, `OnDayPassed(int)`.
- Save : `Capture(out time01, out day)` / `Restore(time01, day)`.

## Fichiers
- `DayPhase.cs` — Night / Dawn / Day / Dusk
- `DayNightProfile.cs` — l'aspect visuel (SO : gradients + courbe)
- `DayNightCycle.cs` — le cycle (MonoBehaviour)
- `Examples/DayNightDemo.cs` — pause / minuit / midi au clavier
- `Explications/` — documentation détaillée

## Brancher sur tes autres systèmes
- **Wave Spawner** : la nuit → lancer les vagues ; l'aube → les stopper.
- **Shop** : ouvert le jour, fermé la nuit (via `OnPhaseChanged`).
- **Save System** : sauvegarde `Capture(out t, out day)`.
- **Event Bus** : republier `OnPhaseChanged` en `TimeOfDayEvent`.
- **Audio** : ambiance jour ↔ nuit (musique/sons) sur `OnPhaseChanged`.

## Notes
- Le soleil tourne en temps réel (pas de saccade). Pour une lune, ajoute une 2e
  lumière orientée à l'opposé.
- Le look dépend **entièrement** des gradients/courbe du profil : c'est là que se
  règle l'ambiance. Sans profil assigné, seul le soleil tourne.

## Tester la démo
Directional Light + un profil (gradients réglés). Sur un GameObject :
`DayNightCycle` (assigne soleil + profil, `dayLengthSeconds` = 20 pour aller
vite) + `DayNightDemo`. Lance : le soleil tourne, l'ambiance change ; P met en
pause, N/M sautent à minuit/midi. La Console logge heures et phases.
