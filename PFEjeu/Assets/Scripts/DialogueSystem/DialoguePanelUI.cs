using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Core.Village;

namespace Core.DialogueSystem
{
    /// <summary>
    /// Panneau de dialogue minimal : affiche une suite de répliques, avance à
    /// chaque appui sur Interact (New Input System, via VillageInputReader).
    /// Option : la dernière réplique devient une question à deux choix —
    /// Interact (E) = confirmer, action « refuser » (Q / bouton Est) = refuser.
    /// </summary>
    public sealed class DialoguePanelUI : MonoBehaviour
    {
        [SerializeField] private VillageInputReader input;

        [Header("UI")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text speakerText;
        [SerializeField] private TMP_Text lineText;
        [SerializeField] private TMP_Text hintText;
        [SerializeField] private string nextHint = "[E] Suivant";
        [SerializeField] private string lastHint = "[E] Fermer";

        [Header("Choix")]
        [Tooltip("Touche de refus. Laisser vide pour utiliser Q / bouton Est de la manette.")]
        [SerializeField] private InputActionReference declineAction;
        [SerializeField] private string confirmText = "Je suis prêt";
        [SerializeField] private string declineText = "Pas encore";

        private InputAction _decline;
        private string[] _lines;
        private int _index;
        private Action _onComplete;
        private Action _onDecline;
        private int _openedFrame = -1;

        public bool IsShown => panel != null && panel.activeSelf;

        private bool IsLastLine => _lines != null && _index >= _lines.Length - 1;
        private bool IsChoice => _onDecline != null;

        private void Awake()
        {
            if (panel != null) panel.SetActive(false);

            _decline = declineAction != null
                ? declineAction.action
                : new InputAction("DialogueDecline", InputActionType.Button);
            if (declineAction == null)
            {
                _decline.AddBinding("<Keyboard>/q");
                _decline.AddBinding("<Gamepad>/buttonEast");
            }
        }

        private void OnEnable()
        {
            if (input != null) input.InteractPressed += HandleInteract;
            _decline.performed += HandleDecline;
            _decline.Enable();
        }

        private void OnDisable()
        {
            if (input != null) input.InteractPressed -= HandleInteract;
            _decline.performed -= HandleDecline;
            _decline.Disable();
        }

        private void OnDestroy()
        {
            if (declineAction == null) _decline?.Dispose();
        }

        /// <summary>
        /// Ouvre le dialogue. <paramref name="onComplete"/> est appelé après la dernière réplique
        /// (ou sur confirmation si <paramref name="onDecline"/> est fourni, auquel cas la dernière
        /// réplique est une question avec deux choix).
        /// </summary>
        public void Show(string speaker, string[] lines, Action onComplete = null, Action onDecline = null)
        {
            if (panel == null || lines == null || lines.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            _lines = lines;
            _index = 0;
            _onComplete = onComplete;
            _onDecline = onDecline;
            _openedFrame = Time.frameCount;

            if (speakerText != null) speakerText.text = speaker;
            panel.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            if (panel != null) panel.SetActive(false);
            _lines = null;
            _onComplete = null;
            _onDecline = null;
        }

        private void HandleInteract()
        {
            // Le même appui qui ouvre le dialogue ne doit pas déjà l'avancer.
            if (!IsShown || _lines == null || Time.frameCount == _openedFrame) return;

            if (!IsLastLine)
            {
                _index++;
                Refresh();
                return;
            }

            Action done = _onComplete;
            Hide();
            done?.Invoke();
        }

        private void HandleDecline(InputAction.CallbackContext _)
        {
            if (!IsShown || _lines == null || !IsChoice || !IsLastLine) return;

            Action declined = _onDecline;
            Hide();
            declined?.Invoke();
        }

        /// <summary>Nom de la touche de refus tel qu'affiché pour la disposition du clavier (A sur AZERTY).</summary>
        private string DeclineKeyName()
        {
            string name = _decline.GetBindingDisplayString(0);
            return string.IsNullOrEmpty(name) ? "?" : name;
        }

        private void Refresh()
        {
            if (lineText != null) lineText.text = _lines[_index];
            if (hintText == null) return;

            if (!IsLastLine) hintText.text = nextHint;
            else if (IsChoice) hintText.text = "[E] " + confirmText + "     [" + DeclineKeyName() + "] " + declineText;
            else hintText.text = lastHint;
        }
    }
}
