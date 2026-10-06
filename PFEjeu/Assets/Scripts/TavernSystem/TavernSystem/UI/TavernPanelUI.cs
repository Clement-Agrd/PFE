using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.ShopSystem;
using Core.TweenSystem;
using Core;

namespace Core.TavernSystem.UI
{
    /// <summary>
    /// Panneau de la taverne : liste des plats/boissons, coût, et bouton de
    /// commande par ligne. S'ouvre via TavernTrigger (approche + E), se ferme
    /// avec le bouton Fermer. Se met à jour en direct quand l'or change.
    /// </summary>
    public sealed class TavernPanelUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private GameObject panel;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_Text walletLabel;
        [SerializeField] private TMP_Text feedbackLabel;
        [SerializeField] private Transform rowsParent;
        [SerializeField] private TavernDishRowUI rowPrefab;
        [SerializeField] private Button closeButton;

        [Header("Couleurs")]
        [SerializeField] private Color okColor = new(0.45f, 0.85f, 0.45f);
        [SerializeField] private Color errorColor = new(0.95f, 0.4f, 0.35f);

        [Header("Animation")]
        [SerializeField, Min(0f)] private float fadeDuration = 0.15f;

        private Tavern _current;
        private Wallet _watchedWallet;
        private readonly List<TavernDishRowUI> _rows = new();

        public bool IsShown => _current != null;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
        }

        private void Start() => HideImmediate();

        public void Show(Tavern tavern)
        {
            if (tavern == null) return;

            bool wasOpen = _current != null;
            Unwatch();
            _current = tavern;
            Watch(_current);

            if (panel != null) panel.SetActive(true);
            Refresh();

            if (!wasOpen)
            {
                GameFreeze.RequestPause(this);
                GameFreeze.RequestCursorUnlock(this);
                PlayOpenAnimation();
            }
        }

        public void Hide()
        {
            if (_current == null) return;
            Unwatch();
            _current = null;
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);

            if (panelGroup == null || fadeDuration <= 0f)
            {
                HideImmediate();
                return;
            }

            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;
            TweenManager.Kill(panelGroup);
            panelGroup.TweenFade(0f, fadeDuration)
                .SetUnscaledTime(true)
                .OnComplete(() => { if (_current == null) HideImmediate(); });
        }

        private void HideImmediate()
        {
            Unwatch();
            _current = null;
            if (panel != null) panel.SetActive(false);
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
        }

        private void PlayOpenAnimation()
        {
            if (panelGroup != null)
            {
                TweenManager.Kill(panelGroup);
                panelGroup.alpha = 0f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
                panelGroup.TweenFade(1f, fadeDuration).SetUnscaledTime(true);
            }
            if (panel != null)
            {
                Transform tr = panel.transform;
                tr.KillTweens();
                tr.localScale = Vector3.one * 0.94f;
                tr.TweenScale(1f, fadeDuration * 1.5f).SetEase(Ease.OutBack).SetUnscaledTime(true);
            }
        }

        private void Watch(Tavern tavern)
        {
            tavern.OnOrdered += HandleOrdered;
            tavern.OnOrderFailed += HandleOrderFailed;

            _watchedWallet = tavern.Wallet;
            if (_watchedWallet != null) _watchedWallet.OnBalanceChanged += HandleBalanceChanged;
        }

        private void Unwatch()
        {
            if (_current != null)
            {
                _current.OnOrdered -= HandleOrdered;
                _current.OnOrderFailed -= HandleOrderFailed;
            }
            if (_watchedWallet != null) _watchedWallet.OnBalanceChanged -= HandleBalanceChanged;
            _watchedWallet = null;
        }

        private void HandleOrdered(TavernDishDefinition dish)
        {
            SetFeedback($"{dish.DishName} commandé !", okColor);
            Refresh();
        }

        private void HandleOrderFailed(string reason) => SetFeedback(reason, errorColor);

        private void HandleBalanceChanged(int _) => Refresh();

        private void Refresh()
        {
            if (_current == null) return;

            int balance = _current.Wallet != null ? _current.Wallet.Balance : 0;
            if (walletLabel != null) walletLabel.text = $"Or : {balance}";

            IReadOnlyList<TavernDishDefinition> dishes = _current.Menu;
            if (rowsParent == null || rowPrefab == null) return;

            EnsureRows(dishes.Count);
            for (int i = 0; i < dishes.Count; i++)
                _rows[i].Set(dishes[i], balance, HandleOrderClicked);
        }

        private void HandleOrderClicked(TavernDishDefinition dish) => _current?.Order(dish);

        private void EnsureRows(int count)
        {
            while (_rows.Count < count) _rows.Add(Instantiate(rowPrefab, rowsParent));
            for (int i = 0; i < _rows.Count; i++) _rows[i].gameObject.SetActive(i < count);
        }

        private void SetFeedback(string text, Color color)
        {
            if (feedbackLabel == null) return;
            feedbackLabel.text = text;
            feedbackLabel.color = color;
        }
    }
}
