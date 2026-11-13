using UnityEngine;

namespace Core.Village.UI
{
    internal static class MapPanelLayout
    {
        private const float Margin = 18f;
        private const float TargetGap = 50f;
        private const float PanelGap = 14f;

        // The cards share a parent, so their positions use the same canvas coordinates.
        internal static bool Place(VillageViewController view, Vector3 position, RectTransform panel,
            RectTransform comparison = null)
        {
            if (view == null || !view.IsInVillageView || view.MapCamera == null ||
                panel.parent is not RectTransform parent) return false;

            Vector3 screen = view.MapCamera.WorldToScreenPoint(position);
            if (screen.z <= 0f) return false;
            Canvas canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect viewport = view.MapCamera.pixelRect;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out var target);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, viewport.min, uiCamera, out var minimum);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, viewport.max, uiCamera, out var maximum);

            float left = minimum.x + Margin;
            float right = maximum.x - Margin;
            float bottom = minimum.y + Margin;
            float top = maximum.y - Margin;
            bool opensRight = screen.x <= viewport.center.x;
            float innerEdge = opensRight ? Mathf.Max(left, target.x + TargetGap) : Mathf.Min(right, target.x - TargetGap);
            float availableWidth = opensRight ? right - innerEdge : innerEdge - left;
            Vector2 panelSize = panel.rect.size;
            Vector2 comparisonSize = comparison != null ? comparison.rect.size : Vector2.zero;
            float gap = comparison != null ? PanelGap : 0f;
            float groupWidth = panelSize.x + gap + comparisonSize.x;
            float groupHeight = Mathf.Max(panelSize.y, comparisonSize.y);
            if (availableWidth <= 0f || groupWidth <= 0f || groupHeight <= 0f) return false;

            float scale = Mathf.Min(1f, Mathf.Min(availableWidth / groupWidth, (top - bottom) / groupHeight));
            float halfHeight = groupHeight * scale * .5f;
            float y = Mathf.Clamp(target.y, bottom + halfHeight, top - halfHeight);
            float direction = opensRight ? 1f : -1f;
            SetCenter(panel, parent, new Vector2(innerEdge + direction * panelSize.x * scale * .5f, y), scale);
            if (comparison != null)
            {
                float offset = panelSize.x + gap + comparisonSize.x * .5f;
                SetCenter(comparison, parent, new Vector2(innerEdge + direction * offset * scale, y), scale);
            }
            return true;
        }

        private static void SetCenter(RectTransform card, RectTransform parent, Vector2 center, float scale)
        {
            // Keep the card's authored anchors and pivot, including stretched prefab layouts.
            Vector2 anchor = new Vector2(
                Mathf.Lerp(card.anchorMin.x, card.anchorMax.x, card.pivot.x),
                Mathf.Lerp(card.anchorMin.y, card.anchorMax.y, card.pivot.y));
            Vector2 anchorPosition = parent.rect.min + Vector2.Scale(parent.rect.size, anchor);
            Vector2 pivotOffset = Vector2.Scale(card.rect.size, card.pivot - Vector2.one * .5f) * scale;
            card.localScale = Vector3.one * scale;
            card.anchoredPosition = center + pivotOffset - anchorPosition;
        }
    }
}
