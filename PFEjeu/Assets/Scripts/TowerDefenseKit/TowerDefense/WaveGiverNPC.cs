using TMPro;
using UnityEngine;
using Core.DialogueSystem;
using Core.Village;
using Core.WaveSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// PNJ qui lance les vagues : le joueur s'approche, appuie sur Interact (E),
    /// le PNJ parle puis demande « prêt ? ». Réponse positive : la vague suivante
    /// démarre. Une nouvelle vague n'est proposée que lorsque la précédente est
    /// terminée. La portée est un test de distance (insensible aux téléportations).
    /// </summary>
    public sealed class WaveGiverNPC : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private TowerDefenseLevel level;
        [SerializeField] private DialoguePanelUI dialogue;
        [SerializeField] private VillageInputReader input;
        [Tooltip("Le joueur (Transform racine). Si vide, cherché via le tag Player.")]
        [SerializeField] private Transform player;

        [Header("Portée")]
        [SerializeField, Min(0.5f)] private float interactRadius = 3.5f;

        [Header("Prompt")]
        [SerializeField] private GameObject promptUI;
        [SerializeField] private string promptMessage = "[E] Parler";

        [Header("Dialogue")]
        [SerializeField] private string npcName = "Capitaine";
        [Tooltip("Dit quand aucune vague n'est en cours. La DERNIÈRE réplique est la question (prêt / pas prêt).")]
        [SerializeField, TextArea(2, 4)] private string[] readyLines =
        {
            "Les éclaireurs signalent des ennemis à l'horizon.",
            "Es-tu prêt à défendre le village ?"
        };
        [Tooltip("Dit si le joueur répond « Je suis prêt ».")]
        [SerializeField, TextArea(2, 4)] private string[] acceptLines =
        {
            "Alors en position, la nuit tombe !"
        };
        [Tooltip("Dit si le joueur répond « Pas encore ».")]
        [SerializeField, TextArea(2, 4)] private string[] declineLines =
        {
            "Prends ton temps. Reviens me voir quand tu seras prêt."
        };
        [Tooltip("Dit pendant qu'une vague est en cours.")]
        [SerializeField, TextArea(2, 4)] private string[] waveActiveLines =
        {
            "Concentre-toi sur le combat, je te dirai quand la suite arrive !"
        };
        [Tooltip("Dit quand toutes les vagues sont terminées.")]
        [SerializeField, TextArea(2, 4)] private string[] allDoneLines =
        {
            "Le village est sauf pour aujourd'hui. Merci, héros."
        };

        private TMP_Text _promptText;

        private bool PlayerInRange
        {
            get
            {
                if (player == null)
                {
                    GameObject found = GameObject.FindGameObjectWithTag("Player");
                    if (found != null) player = found.transform;
                }
                if (player == null) return false;

                Vector3 d = player.position - transform.position;
                d.y = 0f;
                return d.sqrMagnitude <= interactRadius * interactRadius;
            }
        }

        private void Awake()
        {
            if (promptUI != null) _promptText = promptUI.GetComponentInChildren<TMP_Text>(true);
        }

        private void OnEnable()
        {
            if (input != null) input.InteractPressed += HandleInteract;
            if (waveSpawner != null) waveSpawner.OnWaveStarted += HandleWaveStarted;
        }

        private void OnDisable()
        {
            if (input != null) input.InteractPressed -= HandleInteract;
            if (waveSpawner != null) waveSpawner.OnWaveStarted -= HandleWaveStarted;
            if (promptUI != null) promptUI.SetActive(false);
        }

        private void Update()
        {
            bool inRange = PlayerInRange;

            // Joueur parti (ou téléporté) : on ferme le dialogue.
            if (!inRange && dialogue != null && dialogue.IsShown) dialogue.Hide();

            bool show = inRange && dialogue != null && !dialogue.IsShown;
            if (promptUI != null && promptUI.activeSelf != show)
            {
                if (show && _promptText != null) _promptText.text = promptMessage;
                promptUI.SetActive(show);
            }
        }

        private void HandleInteract()
        {
            if (!PlayerInRange || dialogue == null || dialogue.IsShown || waveSpawner == null) return;

            if (waveSpawner.IsWaveActive)
                dialogue.Show(npcName, waveActiveLines);
            else if (!waveSpawner.HasMoreWaves)
                dialogue.Show(npcName, allDoneLines);
            else
                dialogue.Show(npcName, readyLines, OnAccepted, OnDeclined);
        }

        private void OnAccepted()
        {
            if (acceptLines != null && acceptLines.Length > 0)
                dialogue.Show(npcName, acceptLines, LaunchWave);
            else
                LaunchWave();
        }

        private void OnDeclined()
        {
            if (declineLines != null && declineLines.Length > 0)
                dialogue.Show(npcName, declineLines);
        }

        private void LaunchWave()
        {
            // Re-vérifie : l'état a pu changer pendant le dialogue.
            if (waveSpawner == null || !waveSpawner.CanStartWave) return;

            if (level != null) level.StartNextWave();
            else waveSpawner.StartNextWave();
        }

        private void HandleWaveStarted(int _)
        {
            // La vague démarre (et le joueur est téléporté) : plus de texte à l'écran.
            if (dialogue != null && dialogue.IsShown) dialogue.Hide();
            if (promptUI != null) promptUI.SetActive(false);
        }
    }
}
