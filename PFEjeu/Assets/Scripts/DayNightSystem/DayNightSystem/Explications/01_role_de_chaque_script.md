# 01 — Rôle de chaque script

---

## `DayPhase.cs` — les phases
`Night / Dawn / Day / Dusk`. Ce que ton gameplay écoute (spawn de nuit, boutique
de jour...).

---

## `DayNightProfile.cs` — l'aspect (SO)
**Rôle :** décrire à quoi ressemble le ciel/la lumière à chaque heure.

```csharp
Gradient sunColor;        // couleur du soleil selon Time01
AnimationCurve sunIntensity; // intensité selon Time01
Gradient ambientColor;    // lumière ambiante selon Time01
bool driveAmbient;
```

Les méthodes `Evaluate...(t)` renvoient la valeur à l'heure `t` (0→1). Toute
l'ambiance se règle ici, visuellement, sans code.

---

## `DayNightCycle.cs` — le cœur
**Rôle :** faire avancer le temps et appliquer le tout.

**Avancer :**
```csharp
_time01 += Time.deltaTime / dayLengthSeconds;
while (_time01 >= 1) { _time01 -= 1; _dayCount++; OnDayPassed(_dayCount); }
```
Une journée dure `dayLengthSeconds` secondes réelles ; au passage de 1, on
recommence et on compte un jour.

**Appliquer le visuel :**
```csharp
sun.transform.rotation = Euler(_time01 * 360 - 90, 170, 0);  // le soleil tourne
sun.color = profile.EvaluateSunColor(_time01);
sun.intensity = profile.EvaluateSunIntensity(_time01);
RenderSettings.ambientLight = profile.EvaluateAmbient(_time01);
```
Le `× 360 - 90` fait qu'à 6h (0.25) le soleil est à l'horizon et à midi (0.5) au
zénith.

**Détecter les changements :**
```csharp
DetectHour  → nouvelle heure entière ? → OnHourChanged(h)
DetectPhase → nouvelle phase ? → OnPhaseChanged(p)
```

**Contrôle :** `Paused`, `SetTime(hour)` (dormir, cinématique), `SetTime01(t)`.
**Save :** `Capture`/`Restore` (heure + jour).

---

## `Examples/DayNightDemo.cs`
P = pause, N = minuit, M = midi. Logge heures, phases, jours. Montre les
abonnements aux events.

Lis ensuite `02_flux_du_temps.md`.
