# 02 — Le flux d'un changement de météo, étape par étape

---

## Changer de météo

```
weatherController.SetWeather(pluie, 4f)
      │
      ├─ profil nul ou déjà courant ? → on ignore
      │
      ├─ SwapParticles(pluie) :
      │      détruit les particules actuelles (s'il y en a)
      │      instancie le prefab de pluie sous particleParent (la caméra)
      │
      ├─ from = météo actuelle ; Current = pluie
      │
      ├─ StartCoroutine(BlendFog(from, pluie, 4s))   ← fondu du brouillard
      │
      └─ OnWeatherChanged(pluie)   → audio de pluie, gameplay (feu éteint...)
```

---

## Le fondu du brouillard (sur la durée)

```
BlendFog(dégagé → brouillard, 4 s)
      │
      ├─ fromDensity = 0     (dégagé n'a pas de fog)
      ├─ toDensity   = 0.05  (brouillard dense)
      ├─ RenderSettings.fog = true    (allumé pendant tout le fondu)
      │
      └─ sur 4 s :
             fogDensity : 0 → 0.05 progressivement
             fogColor   : ancienne → nouvelle
      │
      └─ à la fin : ApplyFinalFog(brouillard)  → fog reste on, densité 0.05
```

Résultat : le brouillard **monte** doucement en 4 secondes. Pour l'inverse
(brouillard → dégagé), la densité descend vers 0, puis `fog` s'éteint à la fin.

---

## Exemple : dégagé → pluie → brouillard

```
Départ : Dégagé (pas de fog, pas de particules)

SetWeather(Pluie, 4)
   particules de pluie apparaissent sous la caméra
   fog reste à ~0 (Pluie n'a pas de fog fort)
   OnWeatherChanged(Pluie)

SetWeather(Brouillard, 6)
   particules de pluie détruites
   fog monte de ~0 à 0.05 sur 6 s
   OnWeatherChanged(Brouillard)
```

Chaque changement échange proprement les particules et fait transiter le
brouillard.

---

## Le scheduler, en boucle

```
Start : timer = Random(20, 60)

Update :
   timer -= dt
   si timer <= 0 :
       next = tirage pondéré (Dégagé 60, Pluie 25, Brouillard 15)
       SetWeather(next, 4)
       timer = Random(20, 60)   → prochain changement dans 20 à 60 s
```

La météo évolue d'elle-même, avec des durées variables et des probabilités
réglées par les poids.

---

## Le vent, exposé mais pas imposé

```
weatherController.WindStrength   // renvoie le vent de la météo courante

// D'autres systèmes le LISENT :
tree.SwayAmount = weatherController.WindStrength;
windZone.windMain = weatherController.WindStrength;
```

Le système ne « fait » pas le vent : il l'expose. À tes arbres, WindZone, voiles,
particules de le consommer. Découplage.

Lis ensuite `03_choix_de_conception.md`.
