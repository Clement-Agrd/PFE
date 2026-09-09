# 03 — Choix de conception (et leurs limites)

---

## Pourquoi séparer météo et jour/nuit ?

Deux domaines distincts :
- **Day/Night** pilote le **soleil** (rotation, couleur, intensité) et l'**ambiante**.
- **Weather** pilote le **brouillard**, les **particules** et le **vent**.

Ils ne touchent pas aux mêmes réglages → aucun conflit, et ils se **combinent**
(une nuit brumeuse = cycle en phase Nuit + météo Brouillard). Les fusionner en un
seul gros système les rendrait rigides ; séparés, chacun reste simple et
réutilisable.

**Point d'attention :** si un jour tu veux que la météo assombrisse aussi la
lumière (gros orage en plein jour), fais-lui moduler l'intensité du soleil — mais
alors coordonne avec le Day/Night pour ne pas se marcher dessus. Par défaut, on
évite ça.

---

## Pourquoi des transitions en fondu ?

Un changement instantané de brouillard « claque » et casse l'immersion. Mélanger
la densité/couleur sur quelques secondes donne l'impression que le temps
**évolue**. C'est peu de code (un lerp dans une coroutine) pour un gain d'ambiance
énorme.

---

## Le brouillard passe par RenderSettings : la limite pipeline

`RenderSettings.fog` est le brouillard **Built-in / URP**. En **HDRP**, le
brouillard se règle via un **Volume** (Fog override), pas par `RenderSettings` →
ce code n'aura pas d'effet visible sur HDRP. Sur HDRP, il faut piloter un
paramètre de Volume à la place (structure identique, cible différente). Dis-moi
ton pipeline et je fournis la variante.

Les **particules** et le **vent**, eux, sont indépendants du pipeline.

---

## Pourquoi instancier/détruire les particules (pas de pool) ?

Il n'y a qu'**un** système de particules météo à la fois, et les changements sont
rares (toutes les dizaines de secondes). Le coût d'un `Instantiate`/`Destroy`
ponctuel est négligeable — pas besoin de pooling ici (contrairement aux balles ou
ennemis). Si tu changes de météo très souvent, on pourra pooler.

---

## Le vent est exposé, pas appliqué : pourquoi ?

« Faire du vent » dépend de ton jeu (arbres animés, WindZone Unity, voiles, herbe,
particules...). Le système météo se contente d'**exposer** `WindStrength` ; à
chaque consommateur de le lire. Ça évite de coupler la météo à une implémentation
de vent précise.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **transition de particules en fondu** (elles apparaissent/disparaissent
  d'un coup) : pour un fondu, module l'emission rate du système sur la durée.
- Pas d'**effets gameplay** (glisser sous la pluie, gel) : écoute
  `OnWeatherChanged` et applique tes règles.
- Pas de **zones météo locales** (il pleut ici mais pas là) : c'est un état
  **global**. Des zones locales sont un autre système.
- Pas de **HDRP** clé en main (voir plus haut).
- Pas de **son** intégré : branche l'ambiance sur `OnWeatherChanged`.

---

## Intégration avec les autres packages

| Système | Lien |
|---|---|
| **Day/Night** | domaines séparés ; combine nuit + météo ; lie via `OnPhaseChanged` |
| **Audio** | `OnWeatherChanged` → ambiance sonore (pluie, vent) |
| **Save System** | sauvegarde l'id de la météo courante |
| **Event Bus** | republier `OnWeatherChanged` en `WeatherChangedEvent` |
| **Gameplay** | la pluie éteint un feu, réduit la visibilité, etc. |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Data-driven**         | états météo en profils SO                             |
| **Transitions douces**  | fondu du brouillard, pas de coupure                   |
| **Séparation des rôles**| météo ≠ cycle jour/nuit (pas de conflit)              |
| **Découplage**          | vent exposé, event pour le gameplay                   |
| **Honnêteté**           | limite HDRP du fog signalée + solution                |
