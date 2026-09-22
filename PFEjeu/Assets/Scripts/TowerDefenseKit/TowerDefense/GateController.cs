using UnityEngine;
using Core.TweenSystem;
using Core.WaveSystem;
using UnityEngine.AI;

namespace Core.Village
{
    /// <summary>
    /// Contrôle la herse à l'entrée du village : coulisse verticalement (monte
    /// pour s'ouvrir, descend pour se fermer). Se ferme automatiquement au début
    /// d'une vague (phase de défense) et se rouvre quand toutes les vagues du
    /// set sont terminées (retour à la phase de construction).
    ///
    /// Optionnel : si un NavMeshObstacle est assigné, il est activé (carve) quand
    /// la herse est fermée pour bloquer physiquement le passage des ennemis dans
    /// le pathfinding, et désactivé quand elle est ouverte.
    /// </summary>
    public sealed class GateController : MonoBehaviour
    {
        [Header("Référence")]
        [Tooltip("Le mesh de la herse qui coulisse (PAS le collider fixe du rempart).")]
        [SerializeField] private Transform gate;

        [Header("Positions (locales, en Y)")]
        [Tooltip("Hauteur locale Y quand la herse est fermée (au sol).")]
        [SerializeField] private float closedY = 0f;
        [Tooltip("Hauteur locale Y quand la herse est ouverte (relevée).")]
        [SerializeField] private float openY = 4f;

        [Header("Animation")]
        [SerializeField, Min(0.1f)] private float moveDuration = 1.2f;
        [SerializeField] private Ease ease = Ease.InOutCubic;

        [Header("Blocage du pathfinding (optionnel)")]
        [Tooltip("Si assigné, bloque/débloque le passage des ennemis en même temps que la herse.")]
        [SerializeField] private NavMeshObstacle navObstacle;

        [Header("Déclenchement automatique")]
        [Tooltip("Le spawner de vagues à écouter pour fermer/ouvrir automatiquement.")]
        [SerializeField] private WaveSpawner waveSpawner;

        public bool IsOpen { get; private set; } = true;

        private void Start()
        {
            // Position de départ : ouverte (phase de village), sans animation.
            if (gate != null)
            {
                Vector3 pos = gate.localPosition;
                pos.y = openY;
                gate.localPosition = pos;
            }
            IsOpen = true;
            SetObstacleActive(false);
        }

        private void OnEnable()
        {
            if (waveSpawner == null) return;

            waveSpawner.OnWaveStarted += HandleWaveStarted;
            waveSpawner.OnAllWavesCompleted += HandleAllWavesCompleted;
        }

        private void OnDisable()
        {
            if (waveSpawner == null) return;

            waveSpawner.OnWaveStarted -= HandleWaveStarted;
            waveSpawner.OnAllWavesCompleted -= HandleAllWavesCompleted;
        }

        // Appelé à CHAQUE vague (1, 2, 3...) : sans effet si déjà fermée, donc
        // la herse reste simplement close pendant toute la session de défense.
        private void HandleWaveStarted(int waveNumber) => Close();

        // Appelé une fois toutes les vagues du set épuisées (fin de la défense).
        private void HandleAllWavesCompleted() => Open();

        /// <summary>Ferme la herse (phase de défense). Sans effet si déjà fermée.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            if (gate != null)
                gate.TweenLocalMove(new Vector3(gate.localPosition.x, closedY, gate.localPosition.z), moveDuration)
                    .SetEase(ease);

            SetObstacleActive(true);
        }

        /// <summary>Ouvre la herse (phase de construction). Sans effet si déjà ouverte.</summary>
        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            if (gate != null)
                gate.TweenLocalMove(new Vector3(gate.localPosition.x, openY, gate.localPosition.z), moveDuration)
                    .SetEase(ease);

            SetObstacleActive(false);
        }

        private void SetObstacleActive(bool blocking)
        {
            if (navObstacle != null)
                navObstacle.enabled = blocking;
        }
    }
}