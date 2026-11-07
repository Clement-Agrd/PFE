# Validation du relief

## Splines (pente echantillonnee tous les 1 m)

| Route | Longueur 3D (m) | Pente max (deg) | Pente moy. des segments > 5 deg | Ecart max au terrain (m) | Fin -> herse (m) |
|---|---|---|---|---|---|
| Spline  | 185,9 | 11,1 | 8,7 | 0,05 | 8,0 |
| Spline (1)  | 181,8 | 10,5 | 9,1 | 0,08 | 6,6 |
| Spline (2)  | 160,3 | 11,0 | 9,4 | 0,08 | 10,0 |
| Spline (3)  | 201,3 | 11,0 | 8,6 | 0,07 | 8,7 |

## Objets

- Slots hors sol (> 0,06 m) : 0
- Torches enterrees ou flottantes : 0

## Carte de pentes globale (zone sculptee + marge)

- Surface modifiee : 120084 m2 ; part > 20 deg dans le terrain d'origine sur ces memes cellules : 38,6 %
- Nouvelle surface > 15 deg : 27,2 %, > 20 deg : 28,5 %, > 30 deg : 31,2 %
- Pente max : 84,2 deg en (-26.75, -3.00)
- Marche max a la frontiere (courant vs origine) : 110,13 m
- **ECHEC global** (criteres : < 2 % > 20 deg, 0 % > 30 deg, marche <= 0,2 m)
- Cellules de trou (Terrain Holes) : 0

## Objets enregistres (enterres > 0,05 m / flottants > 0,3 m)

- Player : ecart a l'offset 0,00 m OK
- herse : ecart a l'offset 0,00 m OK
- NPC_Capitaine : ecart a l'offset 0,00 m OK
- batiment village : ecart a l'offset 0,00 m OK
- HotelDeVille : ecart a l'offset 0,00 m OK
- Taverne : ecart a l'offset 0,00 m OK
- Scierie : ecart a l'offset 0,00 m OK
- Forge : ecart a l'offset 0,00 m OK
- Mine : ecart a l'offset 0,00 m OK
- Poste_Expedition : ecart a l'offset 0,00 m OK
- Ferme : ecart a l'offset 0,00 m OK
- Proto_Table_ : ecart a l'offset 0,00 m OK
- Forgeron : ecart a l'offset 0,00 m OK
- Prefabs_Barracks : ecart a l'offset 0,00 m OK
- HDV : ecart a l'offset 0,00 m OK
- Tavern : ecart a l'offset 0,00 m OK
- Scirie : ecart a l'offset 0,00 m OK
- Forge : ecart a l'offset 0,00 m OK
- Mine : ecart a l'offset 0,00 m OK
- Camp de recrutement : ecart a l'offset 0,00 m OK
- Centre d'expedition : ecart a l'offset 0,00 m OK
- Ferme : ecart a l'offset 0,00 m OK

## NavMesh (chemin complet spawn -> herse)

- Spawn_Route_01 : OK
- Spawn_Route_02 : OK
- Spawn_Route_03 : OK
- Spawn_Route_04 : OK
- Caserne -> lane R01 : OK

## Plaine apres chaque spawn (40 premiers metres de la lane)

| Route | Y spawn | Y a 40 m | Pente moy. (deg) | Pente max (deg) |
|---|---|---|---|---|
| Spline  | 5,00 | 5,02 | 0,03 | 0,60 |
| Spline (1)  | 5,00 | 5,01 | 0,07 | 0,62 |
| Spline (2)  | 5,00 | 5,26 | 0,42 | 6,05 |
| Spline (3)  | 5,00 | 5,00 | 0,00 | 0,00 |

## Pente par tranche de 20 m, Spline (3) (route 04), d = distance a la fin du spline cote herse

| d (m) | Y debut (spawn) -> fin (herse) | Pente moy. (deg) | Pente moy. hors plat (deg) | Pente max (deg) |
|---|---|---|---|---|
| 180 - 200 | 5,00 -> 5,00 | 0,0 | 0,0 | 0,0 |
| 160 - 180 | 5,00 -> 5,00 | 0,0 | 0,0 | 0,0 |
| 140 - 160 | 5,00 -> 5,61 | 1,7 | 2,7 | 4,1 |
| 120 - 140 | 5,61 -> 6,76 | 3,3 | 3,4 | 4,8 |
| 100 - 120 | 6,76 -> 9,25 | 7,1 | 7,1 | 9,1 |
| 80 - 100 | 9,25 -> 11,88 | 7,5 | 7,5 | 9,0 |
| 60 - 80 | 11,88 -> 14,65 | 7,9 | 7,9 | 10,2 |
| 40 - 60 | 14,65 -> 18,37 | 10,5 | 10,5 | 11,7 |
| 20 - 40 | 18,37 -> 20,95 | 7,3 | 8,0 | 10,5 |
| 0 - 20 | 20,95 -> 20,99 | 0,1 | 0,0 | 0,5 |

- Pente max sur les 4 lanes : 11,9 deg (limite 12) OK
- Pente croissante vers la herse (pente moyenne hors plat, tolerance 1 deg) : OK

## Plateformes de slots : surelevation selon la distance (tri : du plus pres de la base au plus pres des spawns)

| Slot | Dist. depuis spawn (m) | Dist. a la herse (m) | Plateforme | Surelevation mesuree (m) | Cible (m) | Deblai max (m) | Pente max zone (deg) |
|---|---|---|---|---|---|---|---|
| TowerSlot_05_04  | 178 | 11 | oui | 1,11 | 1,25 | 0,01 | 21,8 |
| TowerSlot_05_03  | 160 | 18 | oui | 1,06 | 1,25 | 0,04 | 23,8 |
| TowerSlot_02_06 (ECHEC) | 144 | 37 | oui | 0,18 | 1,25 | 0,39 | 51,6 |
| TowerSlot_05_02  | 134 | 17 | oui | 1,06 | 1,25 | 0,02 | 24,7 |
| TowerSlot_05_01 (ECHEC) | 124 | 26 | oui | 1,38 | 1,01 | 0,05 | 34,8 |
| TowerSlot_01_05 (ECHEC) | 116 | 60 | oui | 0,96 | 1,25 | 0,16 | 36,8 |
| TowerSlot_01_06 (ECHEC) | 114 | 36 | oui | 1,41 | 1,25 | 0,11 | 49,5 |
| TowerSlot_02_05 (ECHEC) | 114 | 52 | oui | 1,02 | 1,25 | 0,39 | 46,8 |
| TowerSlot_01_04 (ECHEC) | 108 | 82 | oui | 0,87 | 1,00 | 0,40 | 36,7 |
| TowerSlot_02_04 (ECHEC) | 92 | 80 | oui | 0,68 | 0,86 | 0,36 | 43,6 |
| TowerSlot_04_03 (ECHEC) | 86 | 91 | oui | 0,49 | 0,58 | 0,40 | 29,2 |
| TowerSlot_03_04 (ECHEC) | 80 | 60 | oui | 0,35 | 0,63 | 0,49 | 41,0 |
| TowerSlot_01_03 (ECHEC) | 70 | 106 | oui | 0,20 | 0,19 | 0,47 | 46,8 |
| TowerSlot_02_03 (ECHEC) | 68 | 95 | oui | 0,20 | 0,16 | 0,44 | 31,9 |
| TowerSlot_03_03 (ECHEC) | 60 | 80 | oui | 0,00 | 0,07 | 0,63 | 43,6 |
| TowerSlot_04_02  | 52 | 133 | oui | 0,01 | 0,00 | 0,46 | 20,9 |
| TowerSlot_01_02  | 48 | 128 | non (a plat) | 0,05 | 0,00 | 0,12 | 23,5 |
| TowerSlot_02_02  | 46 | 124 | non (a plat) | -0,04 | 0,00 | 0,32 | 20,7 |
| TowerSlot_03_02  | 40 | 102 | non (a plat) | 0,00 | 0,00 | 0,36 | 39,9 |
| TowerSlot_04_01  | 26 | 157 | non (a plat) | 0,02 | 0,00 | 0,21 | 17,8 |
| TowerSlot_01_01  | 24 | 155 | non (a plat) | 0,02 | 0,00 | 0,19 | 8,7 |
| TowerSlot_02_01  | 24 | 145 | non (a plat) | -0,06 | 0,00 | 0,20 | 18,9 |
| TowerSlot_03_01  | 20 | 123 | non (a plat) | -0,02 | 0,00 | 0,24 | 22,3 |

Criteres : deblai <= 0,4 m (+ ondulation), pente de bord <= 25 deg, 0 m (pas de plateforme) a moins de 50 m du spawn. La surelevation mesuree inclut l'ondulation (<= 0,4 m).

## Divers

- Altitude minimale du terrain : 5,00 m OK
- muraille (non modifiee) : Y objet 5,41, Y terrain sous le pivot 10,41, ecart -5,00 m

**Resultat : 13 echec(s)**
