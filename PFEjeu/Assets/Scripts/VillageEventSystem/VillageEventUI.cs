using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Core.PauseMenu.PauseUiKit;

namespace Core.VillageEvents
{
    /// <summary>
    /// Fenêtre de dilemme, construite en code (même style que le menu pause). Gèle le jeu
    /// via GameFreeze tant qu'elle est ouverte ; les effets chiffrés sont affichés avant le choix.
    /// </summary>
    public sealed class VillageEventUI : MonoBehaviour
    {
        public struct ChoiceView
        {
            public string label;
            public List<(string text, bool isCost)> effects;
            public bool hasFollowUp;
            public string blockedReason; // null = choix disponible
        }

        private GameObject _canvasGo;
        private Action<int> _onChosen;

        public bool IsOpen => _canvasGo != null;

        public void Show(string title, string flavor, int ritualNight, List<ChoiceView> choices, Action<int> onChosen)
        {
            Close(false);
            _onChosen = onChosen;

            _canvasGo = new GameObject("VillageEventCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Image dim = NewImage("Dim", _canvasGo.transform, Dim);
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            Image card = NewImage("Card", _canvasGo.transform, Card, 24);
            Anchor(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 0f));
            VStack(card.gameObject, 18f, new RectOffset(48, 48, 40, 40));
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            NewText("Night", card.transform, $"NUIT {ritualNight} DU RITUEL", 24f, TextMuted, TextAlignmentOptions.Left, FontStyles.Bold);
            NewText("Title", card.transform, title, 44f, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            NewText("Flavor", card.transform, flavor, 28f, TextMain);

            for (int i = 0; i < choices.Count; i++)
                BuildChoice(card.transform, i, choices[i]);

            GameFreeze.RequestPause(this);
            GameFreeze.RequestCursorUnlock(this);
        }

        private void BuildChoice(Transform parent, int index, ChoiceView view)
        {
            bool blocked = view.blockedReason != null;

            Image bg = NewImage("Choice_" + index, parent, blocked ? Card : CardRaised, 14);
            bg.raycastTarget = true;
            VStack(bg.gameObject, 6f, new RectOffset(28, 28, 16, 16));

            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.interactable = !blocked;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.disabledColor = Color.white;
            button.colors = colors;
            int captured = index;
            button.onClick.AddListener(() => Choose(captured));

            NewText("Label", bg.transform, view.label, 32f, blocked ? TextMuted : TextMain, TextAlignmentOptions.Left, FontStyles.Bold);

            if (view.effects != null)
            {
                foreach ((string text, bool isCost) in view.effects)
                    NewText("Effect", bg.transform, (isCost ? "−  " : "+  ") + text, 26f, isCost ? Danger : Good);
            }

            if (view.hasFollowUp)
                NewText("FollowUp", bg.transform, "Conséquence à venir…", 24f, TextMuted, TextAlignmentOptions.Left, FontStyles.Italic);
            if (blocked)
                NewText("Blocked", bg.transform, view.blockedReason, 24f, Danger, TextAlignmentOptions.Left, FontStyles.Italic);
        }

        private void Choose(int index)
        {
            Action<int> cb = _onChosen;
            Close(true);
            cb?.Invoke(index);
        }

        private void Close(bool release)
        {
            if (_canvasGo != null) Destroy(_canvasGo);
            _canvasGo = null;
            if (release)
            {
                GameFreeze.ReleaseCursorUnlock(this);
                GameFreeze.ReleasePause(this);
            }
        }

        private void OnDisable()
        {
            if (_canvasGo != null) Close(true);
        }
    }
}
