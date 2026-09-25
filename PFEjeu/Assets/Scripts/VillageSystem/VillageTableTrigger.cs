using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Core.TweenSystem;

namespace Core.Village
{
    /// <summary>
    /// Zone d'interaction pour la table de l'HDV : approche-toi, appuie sur E,
    /// la vue village s'active.
    /// La table "encaisse" l'appui (petit écrasement + rebond) juste avant que
    /// la caméra parte, pour qu'on sente que l'input a bien été pris.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class VillageTableTrigger : MonoBehaviour
    {
        [SerializeField] private VillageViewController villageView;
        [Tooltip("Texte affiché quand le joueur est dans la zone (optionnel).")]
        [SerializeField] private GameObject promptUI;
        [Tooltip("Message affiché dans promptUI (si celui-ci a un TMP_Text).")]
        [SerializeField] private string promptMessage = "[E] Accéder à la Table";

        [Header("Feedback à l'appui")]
        [Tooltip("Ce qui bouge quand on appuie. Vide = cet objet.")]
        [SerializeField] private Transform pressVisual;
        [SerializeField, Range(0.8f, 1f)] private float squash = 0.93f;
        [Tooltip("Petit temps mort avant le départ de la caméra, pour voir la table réagir.")]
        [SerializeField, Min(0f)] private float delayBeforeView = 0.15f;
        [SerializeField] private ParticleSystem pressParticles;
        [SerializeField] private AudioSource pressSound;

        private bool _playerInRange;
        private bool _entering;
        private Vector3 _baseScale;
        private Vector3 _promptScale;
        private TMP_Text _promptText;

        private void Awake()
        {
            if (pressVisual == null) pressVisual = transform;
            _baseScale = pressVisual.localScale;
            if (promptUI != null)
            {
                _promptScale = promptUI.transform.localScale;
                _promptText = promptUI.GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void OnEnable()
        {
            if (villageView != null) villageView.OnExitedVillageView += HandleExitedView;
        }

        private void OnDisable()
        {
            if (villageView != null) villageView.OnExitedVillageView -= HandleExitedView;
        }

        private void Update()
        {
            // Le prompt suit _playerInRange à chaque frame, indépendamment des
            // tweens/press/vue village : garantit qu'il s'affiche dès qu'on est
            // à portée, quoi qu'il arrive par ailleurs.
            bool shouldShowPrompt = _playerInRange && !_entering;
            if (promptUI != null && promptUI.activeSelf != shouldShowPrompt)
            {
                if (shouldShowPrompt && _promptText != null) _promptText.text = promptMessage;
                promptUI.SetActive(shouldShowPrompt);
            }

            if (!_playerInRange || _entering) return;
            if (villageView != null && villageView.IsInVillageView) return;

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                Press();
        }

        private void Press()
        {
            _entering = true;

            // Écrasé vers le bas puis ça rebondit à la taille normale.
            pressVisual.KillTweens();
            pressVisual.localScale = new Vector3(_baseScale.x * (2f - squash), _baseScale.y * squash, _baseScale.z * (2f - squash));
            pressVisual.TweenScale(_baseScale, 0.35f).SetEase(Ease.OutElastic);

            if (pressParticles != null) pressParticles.Play();
            if (pressSound != null) pressSound.Play();

            // Le "E" gonfle un coup et disparaît.
            if (promptUI != null)
            {
                Transform prompt = promptUI.transform;
                prompt.KillTweens();
                prompt.TweenScale(_promptScale * 1.2f, 0.08f).SetEase(Ease.OutQuad)
                    .OnComplete(() => prompt.TweenScale(Vector3.zero, 0.12f).SetEase(Ease.InBack)
                        .OnComplete(() => prompt.localScale = _promptScale));
            }

            Invoke(nameof(EnterView), delayBeforeView);
        }

        private void EnterView()
        {
            if (villageView != null) villageView.EnterVillageView();
            else _entering = false;
        }

        private void HandleExitedView()
        {
            _entering = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInRange = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInRange = false;
        }
    }
}
