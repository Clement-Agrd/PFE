# 01 — Rôle de chaque script

---

## `WeatherProfile.cs` — un état météo (SO)
```csharp
bool useFog; Color fogColor; float fogDensity;   // brouillard
GameObject particlePrefab;                        // pluie/neige (optionnel)
float windStrength;                               // vent (lu par d'autres systèmes)
```
Un asset par météo. Décrit **ce à quoi ressemble** un temps, sans code. « Pluie »
= particules + vent ; « Brouillard » = fog dense ; « Dégagé » = rien.

---

## `WeatherController.cs` — le chef d'orchestre
**Rôle :** changer de météo proprement.

```csharp
public void SetWeather(profile, transitionSeconds = -1)
{
    SwapParticles(profile);                       // détruit l'ancien effet, instancie le nouveau
    from = Current ; Current = profile;
    StartCoroutine(BlendFog(from, profile, duration));  // fondu du brouillard
    OnWeatherChanged(profile);
}
```

**Le fondu du brouillard :**
```csharp
BlendFog(from, to, duration)
   fromDensity/fromColor ← ancien (ou 0 si pas de fog)
   toDensity/toColor     ← nouveau
   RenderSettings.fog = true    (actif pendant tout le fondu)
   sur 'duration' : lerp fogDensity et fogColor
   à la fin : ApplyFinalFog(to)  (fog on/off selon le profil final)
```

**Les particules :**
```csharp
SwapParticles(profile)
   détruit l'effet courant
   si le profil a un prefab → l'instancie sous particleParent (la caméra)
```

Expose `Current`, `WindStrength`, et l'event `OnWeatherChanged`.

---

## `WeatherScheduler.cs` — l'évolution automatique
**Rôle :** changer la météo tout seul, au fil du temps.

```csharp
liste de (météo, poids) + durationRange (min/max)

Update :
   timer -= dt
   si timer <= 0 :
       next = tirage pondéré parmi les météos
       controller.SetWeather(next, transition)
       timer = Random(min, max)
```

Le tirage pondéré rend certaines météos fréquentes (Dégagé poids 60) et d'autres
rares (Orage poids 5). Optionnel : sans lui, tu déclenches la météo à la main.

---

## `Examples/WeatherDemo.cs`
1/2/3 → dégagé / pluie / brouillard. Logge les changements. Montre l'usage et
l'abonnement à l'event.

Lis ensuite `02_flux_d_un_changement.md`.
