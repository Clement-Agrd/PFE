using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using static Core.PauseMenu.PauseUiKit;

namespace Core.BarracksSystem
{
    public sealed class HeroInventoryUI : MonoBehaviour
    {
        [SerializeField] private HeroRoster roster;
        private RectTransform _rowsParent;
        private TMP_Text _countLabel, _emptyLabel;
        private readonly List<Row> _rows = new();

        private sealed class Row
        {
            public GameObject Root;
            public TMP_Text Identity, Assignment;
        }

        private void OnEnable()
        {
            if (roster != null) roster.Changed += Refresh;
            BuildUi();
            Refresh();
        }

        private void OnDisable()
        {
            if (roster != null) roster.Changed -= Refresh;
        }

        private void BuildUi()
        {
            if (_rowsParent != null) return;
            var root = NewRect("HeroesPage", transform);
            Stretch(root, 90);
            root.offsetMax = new Vector2(-90, -160);
            var title = NewText("Title", root, "Héros recrutés", 34, TextMain, TextAlignmentOptions.Left, FontStyles.Bold);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(800, 60));
            _countLabel = NewText("HeroCount", root, "", 24, Accent, TextAlignmentOptions.Right);
            Anchor(_countLabel.rectTransform, Vector2.one, Vector2.one, Vector2.zero, new Vector2(500, 60));
            string key = roster != null ? roster.DebugRecruitKey.ToString() : "F8";
            var hint = NewText("DebugRecruitHint", root, $"{key} : recruter un héros aléatoire (debug) · Classe aléatoire · Niveau 1", 22, TextMuted);
            Anchor(hint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -60), new Vector2(1450, 45));

            var scroll = NewRect("HeroScrollView", root);
            Stretch(scroll);
            scroll.offsetMax = new Vector2(0, -125);
            var viewport = NewImage("Viewport", scroll, new Color(1, 1, 1, 0.01f));
            Stretch(viewport.rectTransform);
            viewport.raycastTarget = true;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            _rowsParent = NewRect("Content", viewport.transform);
            _rowsParent.anchorMin = new Vector2(0, 1);
            _rowsParent.anchorMax = Vector2.one;
            _rowsParent.pivot = new Vector2(0.5f, 1);
            _rowsParent.sizeDelta = Vector2.zero;
            VStack(_rowsParent.gameObject, 14);
            _rowsParent.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var sr = scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            sr.viewport = viewport.rectTransform;
            sr.content = _rowsParent;
            sr.horizontal = false;
            sr.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40;
            _emptyLabel = NewText("EmptyHeroes", _rowsParent, $"Aucun héros recruté.\nRecrute au camp de recrutement.\n{key} : recrutement de debug.", 28, TextMuted, TextAlignmentOptions.Center);
            Layout(_emptyLabel.gameObject, height: 150);
        }

        public void Refresh()
        {
            if (_rowsParent == null) return;
            var heroes = roster != null ? roster.OrderedHeroes.ToArray() : System.Array.Empty<heros>();
            int count = heroes.Length;
            _countLabel.text = roster != null && roster.MaxHeroes < int.MaxValue ? $"{count} / {roster.MaxHeroes} héros" : $"{count} héros recruté{(count == 1 ? "" : "s")}";
            _emptyLabel.gameObject.SetActive(count == 0);
            while (_rows.Count < count)
            {
                var card = NewImage("HeroRow" + _rows.Count, _rowsParent, CardRaised, 14);
                Layout(card.gameObject, height: 100);
                var identity = NewText("Identity", card.transform, "", 25, TextMain, TextAlignmentOptions.Left, FontStyles.Bold);
                identity.rectTransform.anchorMin = Vector2.zero;
                identity.rectTransform.anchorMax = new Vector2(0.55f, 1);
                identity.rectTransform.offsetMin = new Vector2(26, 10);
                identity.rectTransform.offsetMax = new Vector2(-12, -10);
                var assignment = NewText("Assignment", card.transform, "", 23, TextMuted, TextAlignmentOptions.Right);
                assignment.rectTransform.anchorMin = new Vector2(0.55f, 0);
                assignment.rectTransform.anchorMax = Vector2.one;
                assignment.rectTransform.offsetMin = new Vector2(12, 10);
                assignment.rectTransform.offsetMax = new Vector2(-26, -10);
                _rows.Add(new Row { Root = card.gameObject, Identity = identity, Assignment = assignment });
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                row.Root.SetActive(i < count);
                if (i >= count) continue;
                var hero = heroes[i];
                row.Identity.text = $"{hero.HeroName}\n{hero.ClasseName} · Niveau {hero.Level} / 5";
                var barracks = hero.AssignedBarracks;
                row.Assignment.text = hero.IsOnExpedition ? $"{(hero.IsExpeditionPending ? "Départ en attente" : "En expédition")} : {hero.ExpeditionName}" : barracks != null
                    ? $"Affecté à : {barracks.name}\nCaserne niveau {barracks.CurrentLevel}"
                    : "Disponible · Aucune affectation";
                row.Assignment.color = barracks != null || hero.IsOnExpedition ? Accent : Good;
            }
        }
    }
}
