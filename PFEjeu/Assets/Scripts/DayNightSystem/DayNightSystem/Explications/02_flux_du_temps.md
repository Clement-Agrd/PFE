# 02 — Le flux du temps, étape par étape

---

## Chaque frame (Update)

```
si en pause → on ne fait rien

_time01 += Time.deltaTime / dayLengthSeconds      ← l'horloge avance
tant que _time01 >= 1 :
      _time01 -= 1 ; _dayCount++ ; OnDayPassed(_dayCount)   ← minuit franchi

ApplyVisual()     → soleil orienté + couleur/intensité + ambiante
DetectHour()      → si l'heure entière a changé → OnHourChanged
DetectPhase()     → si la phase a changé → OnPhaseChanged
```

---

## Le temps normalisé, en clair

```
Time01     Heure     Ce qu'on voit
0.00       0h        minuit, sombre
0.25       6h        lever, soleil à l'horizon, orangé
0.50       12h       midi, soleil au zénith, blanc
0.75       18h       coucher, orangé
1.00 / 0   24h / 0h  minuit à nouveau (nouveau jour)
```

`dayLengthSeconds = 120` → il faut 2 minutes réelles pour parcourir 0 → 1, soit
une journée complète.

---

## L'orientation du soleil

```
angle = _time01 × 360 - 90

Time01 0.25 (6h)  → 0.25×360 - 90 = 0°    → soleil pile à l'horizon (lever)
Time01 0.50 (12h) → 0.50×360 - 90 = 90°   → au zénith (midi)
Time01 0.75 (18h) → 0.75×360 - 90 = 180°  → horizon opposé (coucher)
Time01 0.00 (0h)  → -90°                  → sous l'horizon (nuit)
```

La rotation sur X fait « lever et coucher » le soleil ; le 170 sur Y donne
l'azimut (la direction est/ouest). La couleur et l'intensité, elles, viennent du
profil.

---

## Les events, quand ils tombent

```
OnHourChanged : chaque fois que l'heure entière change (0→1, 1→2...)
                → mettre à jour l'horloge de l'UI

OnPhaseChanged : quand on franchit un seuil de phase
                 → nuit : lâcher les monstres ; jour : ouvrir les commerces

OnDayPassed : à chaque minuit franchi
              → compteur de jours, événements quotidiens, pousses de plantes...
```

---

## Sauter dans le temps (SetTime)

```
cycle.SetTime(20f)     // 20h
      │
      ├─ _time01 = 20/24 = 0.833
      ├─ ApplyVisual()   → le ciel se met tout de suite à l'heure du soir
      ├─ DetectHour()    → OnHourChanged(20)
      └─ DetectPhase()   → OnPhaseChanged(Night) si on change de phase
```

Utile pour « dormir jusqu'au matin », une cinématique, ou un debug. Le visuel et
les events se synchronisent immédiatement.

Lis ensuite `03_choix_de_conception.md`.
