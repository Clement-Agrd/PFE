# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le flux d'un changement de météo et les choix de design.

---

## Le problème qu'on résout

La météo, c'est plusieurs éléments qui changent ensemble et doivent transiter
**en douceur** : le brouillard (couleur, densité), des particules (pluie, neige),
le vent. Passer brutalement de « dégagé » à « brouillard épais » casse
l'immersion. On veut des **états** propres et des **fondus** entre eux.

Et comme pour le jour/nuit, le gameplay doit pouvoir **réagir** (la pluie éteint
un feu de camp, réduit la visibilité...) → un event.

---

## Schéma global

```
        ┌──────────────────────────────┐
        │      WeatherScheduler        │  (optionnel)
        │  choisit une météo au fil    │
        │  du temps (tirage pondéré)   │
        └───────────────┬──────────────┘
                        │ SetWeather
                        ▼
        ┌──────────────────────────────┐
        │      WeatherController        │  (MonoBehaviour)
        │  fondu du brouillard          │
        │  swap des particules          │
        │  OnWeatherChanged + vent      │
        └───────────────┬──────────────┘
                        │ lit
                        ▼
        ┌──────────────────────────────┐
        │      WeatherProfile (SO)     │
        │  brouillard + particules + vent
        └──────────────────────────────┘
```

---

## Les 3 idées de design (le "pourquoi")

### 1. États météo en données (SO)
Chaque météo est un asset : brouillard (on/off, couleur, densité), prefab de
particules, force du vent. On compose « Pluie », « Brouillard », « Blizzard »
dans l'Inspector, réutilisables.

### 2. Transitions en fondu
Changer de météo **mélange** le brouillard (densité + couleur) sur une durée →
il s'installe/se dissipe progressivement. Jamais de coupure sèche.

### 3. Séparation nette avec le Day/Night
La météo gère brouillard + particules + vent. Le cycle jour/nuit gère soleil +
ambiante. Deux domaines distincts → ils cohabitent sans se marcher dessus, et se
combinent (nuit brumeuse, après-midi pluvieux).

---

## En une phrase

> Le controller passe d'un profil météo à un autre en mélangeant le brouillard et
> en échangeant les particules, expose le vent, et prévient le jeu ; un scheduler
> optionnel fait évoluer la météo tout seul.

Lis ensuite `01_role_de_chaque_script.md`.
