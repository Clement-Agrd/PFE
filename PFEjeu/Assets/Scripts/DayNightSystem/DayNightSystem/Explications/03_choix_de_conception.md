# 03 — Choix de conception (et leurs limites)

---

## Pourquoi le temps normalisé (0 → 1) ?

Une seule valeur représente l'heure, et tout en découle :
- avancer = `+= dt / dureeDuJour` ;
- convertir en heures = `× 24` ;
- échantillonner les gradients/courbe = `Evaluate(Time01)` ;
- orienter le soleil = `× 360`.

C'est plus simple et moins buggé que de jongler avec heures/minutes/secondes
séparées. On convertit en heures seulement pour l'affichage.

---

## Pourquoi l'aspect dans un profil (SO) ?

Le **rendu** d'un cycle (les couleurs du lever, la teinte de la nuit) est un
travail d'**artiste**, fait avec des gradients à la souris. Le mettre dans un
asset :
- laisse régler l'ambiance sans toucher au code ni recompiler ;
- permet plusieurs profils (forêt, désert, monde lunaire, saison) échangés d'un
  clic ;
- sépare la **logique** du temps de son **apparence**.

**Sans profil assigné**, seule la rotation du soleil s'applique (pas de couleurs)
— pense à créer et régler un profil, c'est là que tout le look se joue.

---

## Pourquoi piloter la lumière ambiante ?

Sans ambiante, les faces non éclairées par le soleil sont noires la nuit comme le
jour. En faisant varier `RenderSettings.ambientLight`, la scène s'assombrit
globalement la nuit et s'éclaircit le jour → une vraie sensation d'heure. C'est
optionnel (`driveAmbient`).

---

## Le temps réel (deltaTime)

Le cycle avance avec `Time.deltaTime` → il se met en pause avec le jeu
(`timeScale = 0`) et ralentit avec un ralenti. C'est en général voulu. Si tu veux
un temps de jeu indépendant (qui continue en menu pause), passe à
`unscaledDeltaTime` (petit changement).

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **lune / étoiles / skybox** animée : ajoute une 2e Light opposée pour la
  lune, et fais varier le material de skybox sur `Time01` si tu veux un ciel.
- Pas de **météo** (pluie, nuages) : c'est un système à part qui peut s'appuyer
  sur celui-ci.
- Pas de **calendrier** (jours de la semaine, saisons) : `DayCount` te donne le
  numéro du jour, à toi de le mapper si besoin.
- Pas d'**heures de gameplay fines** (rendez-vous à 14h32) : `OnHourChanged` va à
  l'heure entière ; pour la minute, expose et surveille `Hour` toi-même.
- Pas de **lightmaps dynamiques** : le soleil bouge en temps réel (Realtime), pas
  de GI précalculée qui suivrait.

---

## Intégration avec les autres packages

| Système | Lien |
|---|---|
| **Wave Spawner** | nuit → `StartWaves()` ; aube → `StopWaves()` |
| **Shop** | ouvert le jour, fermé la nuit (via `OnPhaseChanged`) |
| **Save System** | `Capture(out t, out day)` |
| **Event Bus** | republier `OnPhaseChanged` en `TimeOfDayEvent` |
| **Audio** | ambiance jour ↔ nuit sur `OnPhaseChanged` |
| **Interaction** | un lit → `SetTime(matin)` pour dormir |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Temps normalisé**     | une valeur 0→1 pilote tout                            |
| **Data-driven**         | aspect en profil SO (gradients + courbe)              |
| **Séparation logique/rendu** | le cycle calcule ; le profil décrit le look      |
| **Découplage / events** | phases/heures/jours → le gameplay réagit sans couplage|
| **Autonome**            | aucune dépendance ; juste une Directional Light       |
