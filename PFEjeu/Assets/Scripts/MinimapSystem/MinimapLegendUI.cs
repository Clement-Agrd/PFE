using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Minimap
{
    /// <summary>
    /// Construit et met à jour la légende de la grande carte : une ligne par
    /// type de marqueur présent dans la scène (icône colorée + libellé),
    /// avec le nombre d'ennemis en direct.
    /// </summary>
    public sealed class MinimapLegendUI : MonoBehaviour
    {
        [SerializeField] private RectTransform rowsParent;
        [SerializeField] private float rowHeight = 34f;

        private readonly Dictionary<MarkerType, TMP_Text> _labels = new();
        private bool _built;

        private void OnEnable()
        {
            if (!_built) Build();
            Refresh();
        }

        private void Update() => Refresh();

        private void Build()
        {
            if (rowsParent == null) rowsParent = (RectTransform)transform;
            foreach (MarkerType t in System.Enum.GetValues(typeof(MarkerType)))
            {
                var row = new GameObject(t.ToString(), typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
                row.transform.SetParent(rowsParent, false);
                row.GetComponent<LayoutElement>().preferredHeight = rowHeight;
                var h = row.GetComponent<HorizontalLayoutGroup>();
                h.spacing = 12f;
                h.childAlignment = TextAnchor.MiddleLeft;
                h.childControlWidth = false;
                h.childControlHeight = false;
                h.childForceExpandWidth = false;
                h.childForceExpandHeight = false;

                var icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                icon.transform.SetParent(row.transform, false);
                var img = icon.GetComponent<Image>();
                img.sprite = MinimapSprites.For(t);
                img.color = MarkerStyle.ColorOf(t);
                img.raycastTarget = false;
                ((RectTransform)icon.transform).sizeDelta = new Vector2(22f, 22f);

                var txt = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                txt.transform.SetParent(row.transform, false);
                var tmp = txt.GetComponent<TextMeshProUGUI>();
                tmp.fontSize = 22f;
                tmp.color = new Color(0.85f, 0.87f, 0.9f);
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.raycastTarget = false;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                ((RectTransform)txt.transform).sizeDelta = new Vector2(300f, rowHeight);

                _labels[t] = tmp;
            }
            _built = true;
        }

        private void Refresh()
        {
            if (!_built) return;
            var present = new HashSet<MarkerType> { MarkerType.Player, MarkerType.Enemy };
            foreach (var m in MinimapMarker.All) present.Add(m.Type);

            foreach (var kv in _labels)
            {
                bool show = present.Contains(kv.Key);
                kv.Value.transform.parent.gameObject.SetActive(show);
                if (!show) continue;

                string text = MarkerStyle.LegendOf(kv.Key);
                if (kv.Key == MarkerType.Enemy)
                {
                    var sys = MinimapSystem.Instance;
                    int n = sys != null ? sys.Enemies.Count : 0;
                    text += $"  ({n})";
                }
                if (kv.Value.text != text) kv.Value.text = text;
            }
        }
    }
}
