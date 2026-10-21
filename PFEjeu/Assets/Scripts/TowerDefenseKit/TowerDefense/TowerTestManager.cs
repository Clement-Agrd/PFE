using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

using Core.InteractionSystem;
using Core.StatsSystem;
using Core.WaveSystem;

namespace Core.TowerDefense
{
    using StatType =
        EnumStats.StatTypes;

    /// <summary>
    /// Banc de test des tours.
    ///
    /// Le joueur s'approche de la console et appuie sur la touche d'interaction :
    /// un panneau s'ouvre. On y choisit, pour chaque type d'ennemi, combien en
    /// faire apparaître sur chaque voie (Path 1, Path 2, boucle), puis on lance
    /// la vague. Le jeu est en pause pendant la configuration.
    /// </summary>
    public sealed class TowerTestManager :
        InteractableBase
    {
        private const int LaneCount = 3;

        private static readonly string[] LaneNames =
        {
            "Path 1",
            "Path 2",
            "Boucle"
        };


        [Serializable]
        public sealed class EnemyOption
        {
            public EnemyDefinition definition;
            public GameObject prefab;
        }


        [Header("Spawner")]

        [Tooltip("Spawner dédié aux vagues personnalisées (toutes les voies).")]
        [SerializeField]
        private WaveSpawner spawner;

        [Tooltip("Id des SpawnZone : Path 1, Path 2, Boucle.")]
        [SerializeField]
        private string[] laneZoneIds =
        {
            "TT_Path1",
            "TT_Path2",
            "TT_Loop"
        };


        [Header("Ennemis disponibles")]

        [SerializeField]
        private List<EnemyOption> enemies =
            new();


        [Header("Réglages de la vague")]

        [Tooltip("Délai entre deux apparitions (s).")]
        [SerializeField, Range(0.05f, 3f)]
        private float spawnInterval = 0.6f;

        [Tooltip("Nombre d'ennemis qui apparaissent en même temps.")]
        [SerializeField, Range(1, 10)]
        private int burstSize = 1;

        [SerializeField, Min(1)]
        private int maxPerCell = 99;


        [Header("Interaction")]

        [Tooltip("Label affiché quand le joueur peut interagir (optionnel).")]
        [SerializeField]
        private GameObject promptLabel;


        private Interactor _interactor;

        private int[,] _counts;

        private bool _open;

        private Rect _windowRect;

        private Vector2 _scroll;

        private GUIStyle _titleStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _centerStyle;
        private GUIStyle _tagPhysStyle;
        private GUIStyle _tagMagStyle;
        private Texture2D _background;


        public override string Prompt =>
            "Ouvrir le banc de test des tours";


        public override bool CanInteract(
            GameObject interactor)
        {
            return isActiveAndEnabled &&
                   !_open;
        }


        public override void Interact(
            GameObject interactor)
        {
            SetOpen(true);
        }


        private void Awake()
        {
            EnsureCounts();
        }


        private void Start()
        {
            SetPromptVisible(false);
        }


        private void OnDisable()
        {
            if (_open)
            {
                SetOpen(false);
            }
        }


        private void Update()
        {
            if (_open)
            {
                if (Keyboard.current != null &&
                    Keyboard.current.escapeKey
                        .wasPressedThisFrame)
                {
                    SetOpen(false);
                }

                SetPromptVisible(false);

                return;
            }

            if (_interactor == null)
            {
                _interactor =
                    FindAnyObjectByType<
                        Interactor
                    >();
            }

            SetPromptVisible(
                _interactor != null &&
                ReferenceEquals(
                    _interactor.Current,
                    this
                )
            );
        }


        // ============================================================
        // ACTIONS
        // ============================================================

        private void SetOpen(
            bool open)
        {
            if (_open == open)
                return;

            _open = open;

            if (open)
            {
                EnsureCounts();

                Core.GameFreeze.RequestPause(this);
                Core.GameFreeze.RequestCursorUnlock(this);
            }
            else
            {
                Core.GameFreeze.ReleasePause(this);
                Core.GameFreeze.ReleaseCursorUnlock(this);
            }
        }


        private void Launch()
        {
            if (spawner == null)
                return;

            List<SpawnEntry> entries =
                new List<SpawnEntry>();

            for (int e = 0; e < enemies.Count; e++)
            {
                if (enemies[e] == null ||
                    enemies[e].prefab == null)
                {
                    continue;
                }

                for (int lane = 0; lane < LaneCount; lane++)
                {
                    int count = _counts[e, lane];

                    if (count <= 0 ||
                        lane >= laneZoneIds.Length)
                    {
                        continue;
                    }

                    entries.Add(
                        new SpawnEntry
                        {
                            prefab = enemies[e].prefab,
                            count = count,
                            zoneId = laneZoneIds[lane],
                            burstSize = burstSize,
                            burstInterval = spawnInterval,
                            startDelay = 0f
                        }
                    );
                }
            }

            if (entries.Count == 0)
                return;

            if (spawner.StartCustomWave(entries))
            {
                SetOpen(false);
            }
        }


        private void Clear()
        {
            for (int e = 0; e < enemies.Count; e++)
            {
                for (int lane = 0; lane < LaneCount; lane++)
                {
                    _counts[e, lane] = 0;
                }
            }
        }


        private void Fill(
            int perCell,
            int lane = -1)
        {
            Clear();

            for (int e = 0; e < enemies.Count; e++)
            {
                for (int l = 0; l < LaneCount; l++)
                {
                    if (lane < 0 ||
                        l == lane)
                    {
                        _counts[e, l] = perCell;
                    }
                }
            }
        }


        private int Total()
        {
            int total = 0;

            for (int e = 0; e < enemies.Count; e++)
            {
                for (int lane = 0; lane < LaneCount; lane++)
                {
                    total += _counts[e, lane];
                }
            }

            return total;
        }


        private void EnsureCounts()
        {
            if (_counts == null ||
                _counts.GetLength(0) != enemies.Count)
            {
                _counts =
                    new int[
                        Mathf.Max(1, enemies.Count),
                        LaneCount
                    ];
            }
        }


        private void SetPromptVisible(
            bool visible)
        {
            if (promptLabel != null &&
                promptLabel.activeSelf != visible)
            {
                promptLabel.SetActive(
                    visible
                );
            }
        }


        // ============================================================
        // PANEL
        // ============================================================

        private void OnGUI()
        {
            if (!_open)
                return;

            BuildStyles();

            // Tout est dessiné pour une hauteur de 720 px, puis mis à l'échelle.
            float scale = Screen.height / 720f;

            GUI.matrix =
                Matrix4x4.Scale(
                    new Vector3(scale, scale, 1f)
                );

            float width = 880f;
            float height = 640f;

            Rect area =
                new Rect(
                    (Screen.width / scale - width) * 0.5f,
                    (720f - height) * 0.5f,
                    width,
                    height
                );

            GUI.DrawTexture(
                area,
                _background
            );

            GUILayout.BeginArea(
                new Rect(
                    area.x + 16f,
                    area.y + 12f,
                    area.width - 32f,
                    area.height - 24f
                )
            );

            GUILayout.Label(
                "BANC DE TEST DES TOURS",
                _titleStyle
            );

            GUILayout.Label(
                "Choisis combien d'ennemis de chaque type partent sur chaque voie.",
                _smallStyle
            );

            GUILayout.Space(6f);

            DrawHeader();

            _scroll =
                GUILayout.BeginScrollView(
                    _scroll,
                    GUILayout.Height(370f)
                );

            for (int e = 0; e < enemies.Count; e++)
            {
                DrawRow(e);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6f);

            DrawSettings();

            GUILayout.Space(6f);

            DrawFooter();

            GUILayout.EndArea();

            GUI.matrix = Matrix4x4.identity;
        }


        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(
                "Ennemi",
                _smallStyle,
                GUILayout.Width(330f)
            );

            for (int lane = 0; lane < LaneCount; lane++)
            {
                GUILayout.Label(
                    LaneNames[lane],
                    _centerStyle,
                    GUILayout.Width(150f)
                );
            }

            GUILayout.EndHorizontal();
        }


        private void DrawRow(
            int index)
        {
            EnemyOption option =
                enemies[index];

            if (option == null ||
                option.definition == null)
            {
                return;
            }

            EnemyDefinition def =
                option.definition;

            GUILayout.BeginHorizontal(
                GUI.skin.box
            );

            // Nom + stats clés + résistances.
            GUILayout.BeginVertical(
                GUILayout.Width(330f)
            );

            GUILayout.Label(
                string.IsNullOrEmpty(def.DisplayName)
                    ? def.name
                    : def.DisplayName,
                _titleStyle
            );

            GUILayout.Label(
                $"PV {def.GetStat(StatType.Life):0}   " +
                $"Vit {def.GetStat(StatType.Speed):0.0}   " +
                $"Dég {def.GetStat(StatType.PhysicDamage):0}/" +
                $"{def.GetStat(StatType.MagicDamage):0}",
                _smallStyle
            );

            GUILayout.BeginHorizontal();

            GUILayout.Label(
                "Phys −" + Reduction(def, StatType.DefensePhysic) + "%",
                _tagPhysStyle
            );

            GUILayout.Label(
                "Mag −" + Reduction(def, StatType.DefenseMagic) + "%",
                _tagMagStyle
            );

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            for (int lane = 0; lane < LaneCount; lane++)
            {
                DrawStepper(index, lane);
            }

            GUILayout.EndHorizontal();
        }


        private void DrawStepper(
            int enemy,
            int lane)
        {
            GUILayout.BeginHorizontal(
                GUILayout.Width(150f)
            );

            GUILayout.FlexibleSpace();

            // Maj = pas de 5.
            int step =
                Event.current.shift
                    ? 5
                    : 1;

            if (GUILayout.Button("−", GUILayout.Width(30f), GUILayout.Height(30f)))
            {
                _counts[enemy, lane] =
                    Mathf.Max(0, _counts[enemy, lane] - step);
            }

            GUILayout.Label(
                _counts[enemy, lane].ToString(),
                _centerStyle,
                GUILayout.Width(36f),
                GUILayout.Height(30f)
            );

            if (GUILayout.Button("+", GUILayout.Width(30f), GUILayout.Height(30f)))
            {
                _counts[enemy, lane] =
                    Mathf.Min(maxPerCell, _counts[enemy, lane] + step);
            }

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();
        }


        private void DrawSettings()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(
                $"Intervalle : {spawnInterval:0.00}s",
                _smallStyle,
                GUILayout.Width(150f)
            );

            spawnInterval =
                GUILayout.HorizontalSlider(
                    spawnInterval,
                    0.05f,
                    3f,
                    GUILayout.Width(220f)
                );

            GUILayout.Space(20f);

            GUILayout.Label(
                $"Par salve : {burstSize}",
                _smallStyle,
                GUILayout.Width(100f)
            );

            burstSize =
                Mathf.RoundToInt(
                    GUILayout.HorizontalSlider(
                        burstSize,
                        1,
                        10,
                        GUILayout.Width(140f)
                    )
                );

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            GUILayout.Label(
                "Préréglages :",
                _smallStyle,
                GUILayout.Width(150f)
            );

            if (GUILayout.Button("Vider"))
                Clear();

            if (GUILayout.Button("1 de chaque (Path 1)"))
                Fill(1, 0);

            if (GUILayout.Button("1 de chaque (Path 2)"))
                Fill(1, 1);

            if (GUILayout.Button("1 de chaque (Boucle)"))
                Fill(1, 2);

            if (GUILayout.Button("Horde ×5 partout"))
                Fill(5);

            GUILayout.EndHorizontal();
        }


        private void DrawFooter()
        {
            bool active =
                spawner != null &&
                spawner.IsWaveActive;

            GUILayout.BeginHorizontal();

            GUILayout.Label(
                active
                    ? $"Vague en cours : {spawner.AliveCount} vivants"
                    : $"Total prévu : {Total()} ennemis",
                _titleStyle,
                GUILayout.Width(300f)
            );

            GUILayout.FlexibleSpace();

            GUI.enabled =
                !active &&
                Total() > 0;

            if (GUILayout.Button("LANCER LA VAGUE", GUILayout.Width(200f), GUILayout.Height(38f)))
                Launch();

            GUI.enabled =
                spawner != null;

            if (GUILayout.Button("Tout retirer", GUILayout.Width(120f), GUILayout.Height(38f)))
                spawner.DismissAll();

            GUI.enabled = true;

            if (GUILayout.Button("Fermer (Échap)", GUILayout.Width(130f), GUILayout.Height(38f)))
                SetOpen(false);

            GUILayout.EndHorizontal();
        }


        // Réduction de dégâts : défense / (défense + 100), comme Health.
        private static int Reduction(
            EnemyDefinition def,
            StatType defense)
        {
            float value =
                def.GetStat(defense);

            return Mathf.RoundToInt(
                value / (value + 100f) * 100f
            );
        }


        private void BuildStyles()
        {
            if (_titleStyle != null)
                return;

            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.11f, 0.96f));
            _background.Apply();

            _titleStyle =
                new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.95f, 0.85f, 0.55f) }
                };

            _smallStyle =
                new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    normal = { textColor = new Color(0.8f, 0.82f, 0.88f) }
                };

            _centerStyle =
                new GUIStyle(_smallStyle)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                    fontStyle = FontStyle.Bold
                };

            _tagPhysStyle =
                new GUIStyle(_smallStyle)
                {
                    normal = { textColor = new Color(1f, 0.6f, 0.4f) }
                };

            _tagMagStyle =
                new GUIStyle(_smallStyle)
                {
                    normal = { textColor = new Color(0.6f, 0.65f, 1f) }
                };
        }
    }
}
