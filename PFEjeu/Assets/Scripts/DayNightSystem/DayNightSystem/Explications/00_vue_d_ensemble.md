# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le flux du temps et les choix de design.

---

## Le problème qu'on résout

Un cycle jour/nuit, c'est deux choses :
- **le temps** qui avance (une horloge de jeu, avec un jour = X secondes réelles) ;
- **l'ambiance** qui suit (le soleil tourne, la lumière passe du bleu nuit à
  l'orangé du lever, au blanc de midi, puis au coucher).

Et le gameplay doit pouvoir **réagir** : les monstres sortent la nuit, les
boutiques ferment, les PNJ rentrent. On veut donc aussi des **events** aux
moments clés.

---

## Schéma global

```
        ┌──────────────────────────────┐
        │        DayNightCycle         │  (MonoBehaviour)
        │  avance le temps (0 → 1)     │
        │  oriente le soleil           │
        │  applique couleur/intensité  │
        │  events (heure/phase/jour)   │
        └───────┬──────────────┬───────┘
                │ pilote        │ lit l'aspect
                ▼               ▼
        ┌──────────────┐  ┌──────────────────────┐
        │  Light (soleil)│  │ DayNightProfile (SO) │
        │  rotation +    │  │  gradient couleur    │
        │  couleur + int.│  │  courbe intensité    │
        └──────────────┘  │  gradient ambiante   │
                          └──────────────────────┘
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Temps normalisé 0 → 1
Une seule valeur `Time01` (0 = minuit, 0.5 = midi, 1 = minuit) représente
l'heure. Facile à faire avancer (`+= dt / dureeDuJour`), à mapper en heures
(`× 24`), et à échantillonner (gradients, courbe).

### 2. Aspect data-driven (profil SO)
Le **look** (couleur du soleil, intensité, ambiante) est dans un asset, réglé
par des **gradients** et une **courbe**. Un artiste dessine l'ambiance à la
souris, sans code. Plusieurs profils = plusieurs biomes/saisons.

### 3. Phases + events pour le gameplay
Le cycle traduit `Time01` en **phases** (Aube/Jour/Crépuscule/Nuit) et émet des
events aux changements. Le gameplay s'abonne (« nuit → spawn ») sans se soucier
du calcul du temps.

---

## En une phrase

> Le cycle fait avancer une horloge normalisée, oriente le soleil et applique
> les couleurs du profil selon l'heure, et prévient le jeu quand l'heure, la
> phase ou le jour changent.

Lis ensuite `01_role_de_chaque_script.md`.
