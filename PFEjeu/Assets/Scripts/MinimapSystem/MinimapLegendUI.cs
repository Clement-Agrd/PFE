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

        private readonly HashSet<MarkerType> _present = new();
        private float _nextRefresh;
        private int _lastEnemyCount = -1;

        private void Update()
        {
            // La légende n'a pas besoin d'être rafraîchie à chaque frame.
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

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
            _present.Clear();
            _present.Add(MarkerType.Player);
            _present.Add(MarkerType.Enemy);
            var all = MinimapMarker.All;
            for (int i = 0; i < all.Count; i++) _present.Add(all[i].Type);

            foreach (var kv in _labels)
            {
                bool show = _present.Contains(kv.Key);
                var rowGo = kv.Value.transform.parent.gameObject;
                if (rowGo.activeSelf != show) rowGo.SetActive(show);
                if (!show) continue;

                // Les libellés fixes ne changent jamais : on ne touche qu'à celui des ennemis, et seulement
                // quand leur nombre change (évite de réallouer une string à chaque rafraîchissement).
                if (kv.Key == MarkerType.Enemy)
                {
                    var sys = MinimapSystem.Instance;
                    int n = sys != null ? sys.Enemies.Count : 0;
                    if (n != _lastEnemyCount)
                    {
                        _lastEnemyCount = n;
                        kv.Value.text = $"{MarkerStyle.LegendOf(kv.Key)}  ({n})";
                    }
                }
                else if (string.IsNullOrEmpty(kv.Value.text))
                {
                    kv.Value.text = MarkerStyle.LegendOf(kv.Key);
                }
            }
        }
    }
}
