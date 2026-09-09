# Weather System

Météo dynamique : brouillard en fondu, particules (pluie/neige), vent, avec
transitions douces et un planificateur optionnel. Compagnon du Day/Night.
Namespace : `Core.WeatherSystem`.

## ⚠️ Note render pipeline
Le brouillard passe par `RenderSettings.fog` :
- **Built-in RP** : fonctionne directement.
- **URP** : active le fog dans les settings du pipeline ; `RenderSettings` reste
  utilisé.
- **HDRP** : le brouillard passe par un **Volume** (Fog override), pas par
  `RenderSettings`. Sur HDRP, pilote un Volume à la place — dis-moi si tu veux la
  variante.

Les particules et le vent marchent quel que soit le pipeline.

## Installation
Dépose le dossier `WeatherSystem/` dans `Assets/`.

## Mise en place
1. Crée des profils : `Assets > Create > Environment > Weather Profile`.
   - Dégagé : `useFog` off, pas de particules.
   - Pluie : particules = ton prefab de pluie, vent > 0.
   - Brouillard : `useFog` on, densité élevée.
2. Sur un GameObject : ajoute `WeatherController`. Assigne le `startWeather` et
   le `particleParent` (souvent la caméra, pour que la pluie suive).
3. (Optionnel) Ajoute `WeatherScheduler` pour que la météo change toute seule
   (liste de profils + poids + durées).

## Utilisation
```csharp
weatherController.SetWeather(rainProfile);        // transition par défaut
weatherController.SetWeather(fogProfile, 6f);     // fondu de 6 s
float wind = weatherController.WindStrength;       // lisible par tes arbres, etc.
weatherController.OnWeatherChanged += w => ...;    // réagir
```

## Le prefab de particules
Crée un Particle System (pluie : beaucoup de petites gouttes tombant vers le bas,
en box émettrice large au-dessus de la caméra ; neige : plus lent, flocons). Fais
un prefab et assigne-le au profil. Le controller l'instancie sous `particleParent`
(la caméra), et le détruit au changement de météo.

## API
- `WeatherController.SetWeather(profile, transition = -1)`, `Current`,
  `WindStrength`, event `OnWeatherChanged(profile)`.
- `WeatherScheduler` : liste pondérée + durées → change la météo automatiquement.

## Fichiers
- `WeatherProfile.cs` — un état météo (SO)
- `WeatherController.cs` — change la météo en fondu (MonoBehaviour)
- `WeatherScheduler.cs` — évolution automatique pondérée (MonoBehaviour)
- `Examples/WeatherDemo.cs` — changer de météo au clavier
- `Explications/` — documentation détaillée

## 🔗 Se marie avec le Day/Night
Météo et cycle jour/nuit gèrent des choses **différentes** → aucun conflit :
- **Day/Night** : soleil (rotation, couleur, intensité) + ambiante.
- **Weather** : brouillard + particules + vent.
Ensemble : une nuit brumeuse, un après-midi pluvieux... Tu peux lier les deux
(ex. brouillard plus dense à l'aube) en écoutant `OnPhaseChanged` du cycle.

## Autres liens
- **Wind** : `WindStrength` → alimente un `WindZone` ou l'agitation de tes arbres.
- **Audio** : `OnWeatherChanged` → ambiance sonore (pluie, vent).
- **Save System** : sauvegarde l'id de la météo courante.
- **Gameplay** : la pluie ralentit, réduit la visibilité, éteint des feux...

## Tester la démo
Crée 3 profils (Dégagé, Pluie avec un prefab de particules, Brouillard dense).
Sur un GameObject : `WeatherController` (assigne un startWeather + la caméra en
particleParent) + `WeatherDemo` (assigne les 3). 1/2/3 pour changer ; regarde le
brouillard se lever/dissiper en douceur.
