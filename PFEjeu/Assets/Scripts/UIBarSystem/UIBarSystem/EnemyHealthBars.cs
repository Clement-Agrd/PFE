using System.Collections.Generic;
using Core.HealthSystem;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Core.UIBarSystem
{
    [DefaultExecutionOrder(1000)]
    internal sealed class EnemyHealthBars : MonoBehaviour
    {
        private const float MaxDistance = 55f;
        private static readonly Dictionary<Scene, EnemyHealthBars> SceneBars = new();

        private sealed class Entry
        {
            public Health Health;
            public Transform Anchor;
            public UIBar Bar;
        }

        private readonly Dictionary<HealthBarLink, Entry> _entries = new();
        private readonly Stack<UIBar> _pool = new();
        private Camera _camera;
        private RectTransform _rect;
        private Sprite _fillSprite;

        internal static EnemyHealthBars ForScene(Scene scene)
        {
            if (SceneBars.TryGetValue(scene, out var existing) && existing != null) return existing;

            var root = new GameObject("Barres de vie ennemies", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -10;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var bars = root.AddComponent<EnemyHealthBars>();
            bars._rect = (RectTransform)root.transform;
            SceneBars[scene] = bars;
            return bars;
        }

        internal void Register(HealthBarLink link, Health health, Transform anchor)
        {
            _entries.Add(link, new Entry { Health = health, Anchor = anchor });
        }

        internal void Unregister(HealthBarLink link)
        {
            if (!_entries.Remove(link, out var entry)) return;
            Release(entry);
        }

        internal void SetValue(HealthBarLink link, int current, int max)
        {
            if (_entries.TryGetValue(link, out var entry) && entry.Bar != null)
                entry.Bar.SetValue(current, max);
        }

        private void LateUpdate()
        {
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera == null)
            {
                foreach (var entry in _entries.Values) Release(entry);
                return;
            }

            Vector3 cameraPosition = _camera.transform.position;
            Rect viewport = _camera.pixelRect;
            foreach (var entry in _entries.Values)
            {
                if (entry.Health == null || entry.Health.IsDead || entry.Anchor == null)
                {
                    Release(entry);
                    continue;
                }

                Vector3 world = entry.Anchor.position;
                if ((world - cameraPosition).sqrMagnitude > MaxDistance * MaxDistance)
                {
                    Release(entry);
                    continue;
                }

                Vector3 screen = _camera.WorldToScreenPoint(world);
                if (screen.z <= 0f || !viewport.Contains(screen))
                {
                    Release(entry);
                    continue;
                }

                if (entry.Bar == null)
                {
                    entry.Bar = _pool.Count > 0 ? _pool.Pop() : CreateBar();
                    entry.Bar.SetValue(entry.Health.CurrentHealth, entry.Health.MaxHealth);
                    entry.Bar.SnapToValue();
                    entry.Bar.gameObject.SetActive(true);
                }

                RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screen, null, out var localPoint);
                var barRect = (RectTransform)entry.Bar.transform;
                if ((barRect.anchoredPosition - localPoint).sqrMagnitude >= .25f)
                    barRect.anchoredPosition = localPoint;
            }
        }

        private void Release(Entry entry)
        {
            if (entry.Bar == null) return;
            entry.Bar.gameObject.SetActive(false);
            _pool.Push(entry.Bar);
            entry.Bar = null;
        }

        private UIBar CreateBar()
        {
            var go = new GameObject("Barre ennemie", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_rect, false);
            rect.sizeDelta = new Vector2(82f, 10f);

            var frame = ImageLayer("Fond", rect, new Color(.10f, .035f, .045f), 0f);
            var delayed = ImageLayer("Dégâts récents", frame.rectTransform, new Color(1f, .72f, .24f), 1f);
            var fill = ImageLayer("PV", frame.rectTransform, new Color(.80f, .08f, .10f), 1f);
            var bar = go.AddComponent<UIBar>();
            bar.ConfigureImages(fill, delayed);
            return bar;
        }

        private Image ImageLayer(string name, RectTransform parent, Color color, float inset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = -Vector2.one * inset;
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = color;
            if (inset > 0f)
            {
                if (_fillSprite == null)
                {
                    var texture = Texture2D.whiteTexture;
                    _fillSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f);
                }
                image.sprite = _fillSprite;
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
            }
            return image;
        }

        private void OnDestroy()
        {
            SceneBars.Remove(gameObject.scene);
            if (_fillSprite != null) Destroy(_fillSprite);
        }
    }
}
