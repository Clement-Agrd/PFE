using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Core.InventorySystem;
using Core.Village;

namespace Core.TrapSystem
{
    /// <summary>
    /// À poser sur le joueur. Clic molette : ouvre une roue listant les pièges de l'inventaire.
    /// Cliquer (gauche ou molette) sur un piège le pose devant le joueur ; clic droit / Échap annule.
    /// Le niveau d'amélioration Forge de l'objet consommé est transmis au piège posé.
    /// </summary>
    public sealed class TrapPlacer : MonoBehaviour
    {
        [SerializeField] private InventoryHolder playerInventory;
        [SerializeField] private ForgeDefinition forgeDefinition;

        [Header("Placement")]
        [SerializeField, Min(0f)] private float placeDistance = 2f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Prévisualisation")]
        [Tooltip("Distance max (depuis le joueur) à laquelle on peut poser.")]
        [SerializeField, Min(1f)] private float maxPlaceRange = 8f;
        [SerializeField, Range(0f, 90f)] private float maxSlope = 35f;
        [SerializeField] private Color validColor = new Color(0.2f, 1f, 0.3f, 0.45f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.2f, 0.2f, 0.45f);

        [Header("Roue")]
        [SerializeField, Min(40f)] private float wheelRadius = 170f;
        [SerializeField, Min(20f)] private float nodeRadius = 62f;
        [SerializeField] private Color nodeColor = new Color(0.1f, 0.1f, 0.12f, 0.85f);
        [SerializeField] private Color nodeHoverColor = new Color(0.85f, 0.55f, 0.1f, 0.95f);

        private sealed class Node
        {
            public int slot;
            public RectTransform rect;
            public Image background;
        }

        private readonly List<Node> _nodes = new();
        private GameObject _wheelRoot;
        private Sprite _circle;

        private int _previewSlot = -1;
        private GameObject _ghost;
        private Material _ghostMaterial;
        private Vector3 _ghostPosition;
        private bool _ghostValid;
        private float _ghostFootprint = 0.6f;
        private bool _open;
        private int _hovered = -1;

        public bool IsWheelOpen => _open;

        /// <summary>Vrai pendant l'aperçu de placement d'un piège.</summary>
        public bool IsPlacing => _previewSlot >= 0;

        /// <summary>Lance l'aperçu de placement du piège de la case (utilisé par la hotbar).</summary>
        public bool TryBeginPlacement(int slotIndex)
        {
            if (_open || _previewSlot >= 0 || Core.GameFreeze.IsCursorUnlocked) return false;
            BeginPreview(slotIndex);
            return _previewSlot >= 0;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            if (_previewSlot >= 0)
            {
                UpdatePreview(mouse);
                return;
            }

            if (!_open)
            {
                if (mouse.middleButton.wasPressedThisFrame) OpenWheel();
                return;
            }

            UpdateHover(mouse.position.ReadValue());

            bool cancel = mouse.rightButton.wasPressedThisFrame
                          || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
            if (cancel) { CloseWheel(); return; }

            if (mouse.leftButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
            {
                int slot = _hovered >= 0 ? _nodes[_hovered].slot : -1;
                CloseWheel();
                if (slot >= 0) BeginPreview(slot);
            }
        }

        private void OnDisable()
        {
            if (_open) CloseWheel();
            EndPreview();
        }

        private void OnDestroy()
        {
            if (_wheelRoot != null) Destroy(_wheelRoot);
            if (_circle != null) Destroy(_circle);
            if (_ghostMaterial != null) Destroy(_ghostMaterial);
        }

        #region Roue

        private void OpenWheel()
        {
            Inventory inv = playerInventory != null ? playerInventory.Inventory : null;
            if (inv == null || Core.GameFreeze.IsCursorUnlocked) return;

            var slots = new List<int>();
            for (int i = 0; i < inv.Capacity; i++)
                if (IsTrap(inv.GetSlot(i))) slots.Add(i);
            if (slots.Count == 0) return;

            BuildWheel(inv, slots);
            _open = true;
            _hovered = -1;
            Core.GameFreeze.RequestCursorUnlock(this);
        }

        private void CloseWheel()
        {
            _open = false;
            if (_wheelRoot != null) _wheelRoot.SetActive(false);
            Core.GameFreeze.ReleaseCursorUnlock(this);
        }

        private void BuildWheel(Inventory inv, List<int> slots)
        {
            if (_wheelRoot != null) Destroy(_wheelRoot);
            _nodes.Clear();

            _wheelRoot = new GameObject("TrapWheel", typeof(Canvas), typeof(CanvasScaler));
            var canvas = _wheelRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            _wheelRoot.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            var center = new GameObject("Center", typeof(RectTransform)).GetComponent<RectTransform>();
            center.SetParent(_wheelRoot.transform, false);
            center.anchorMin = center.anchorMax = new Vector2(0.5f, 0.5f);
            center.anchoredPosition = Vector2.zero;

            _circle ??= MakeCircleSprite();

            for (int n = 0; n < slots.Count; n++)
            {
                ItemStack stack = inv.GetSlot(slots[n]);
                float angle = Mathf.PI * 0.5f - n * (Mathf.PI * 2f / slots.Count);

                var go = new GameObject("Node_" + n, typeof(RectTransform), typeof(Image));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(center, false);
                rect.sizeDelta = Vector2.one * nodeRadius * 2f;
                rect.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * wheelRadius;

                var bg = go.GetComponent<Image>();
                bg.sprite = _circle;
                bg.color = nodeColor;
                bg.raycastTarget = false;

                if (stack.Definition.Icon != null)
                {
                    var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    icon.transform.SetParent(rect, false);
                    var iconRect = (RectTransform)icon.transform;
                    iconRect.sizeDelta = Vector2.one * nodeRadius;
                    var iconImage = icon.GetComponent<Image>();
                    iconImage.sprite = stack.Definition.Icon;
                    iconImage.raycastTarget = false;
                }

                string level = stack.UpgradeLevel > 0 ? $" +{stack.UpgradeLevel}" : "";
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(rect, false);
                var text = label.GetComponent<TextMeshProUGUI>();
                text.text = $"{stack.Definition.DisplayName}{level}\n<size=80%>x{stack.Quantity}</size>";
                text.alignment = TextAlignmentOptions.Center;
                text.fontSize = 14f;
                text.color = Color.white;
                text.raycastTarget = false;
                var labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = new Vector2(nodeRadius * 2f, 40f);
                labelRect.anchoredPosition = new Vector2(0f, -nodeRadius - 22f);

                _nodes.Add(new Node { slot = slots[n], rect = rect, background = bg });
            }

            _wheelRoot.SetActive(true);
        }

        private void UpdateHover(Vector2 mouse)
        {
            Vector2 origin = new Vector2(Screen.width, Screen.height) * 0.5f;
            int best = -1;
            float bestDist = nodeRadius;

            for (int i = 0; i < _nodes.Count; i++)
            {
                float d = Vector2.Distance(mouse - origin, _nodes[i].rect.anchoredPosition);
                if (d <= bestDist) { best = i; bestDist = d; }
            }

            if (best == _hovered) return;
            _hovered = best;
            for (int i = 0; i < _nodes.Count; i++)
            {
                bool hot = i == _hovered;
                _nodes[i].background.color = hot ? nodeHoverColor : nodeColor;
                _nodes[i].rect.localScale = Vector3.one * (hot ? 1.12f : 1f);
            }
        }

        private static Sprite MakeCircleSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        #endregion

        #region Placement

        private static bool IsTrap(ItemStack s)
            => s != null && !s.IsEmpty && s.Definition.Category == ItemCategory.Trap;

        /// <summary>Lance l'aperçu du piège de la case : clic molette = poser, Échap / clic droit = annuler.</summary>
        private void BeginPreview(int slotIndex)
        {
            Inventory inv = playerInventory != null ? playerInventory.Inventory : null;
            if (inv == null || slotIndex < 0 || slotIndex >= inv.Capacity) return;

            ItemStack stack = inv.GetSlot(slotIndex);
            if (!IsTrap(stack)) return;

            GameObject prefab = stack.Definition.WorldPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[TrapPlacer] {stack.Definition.DisplayName} n'a pas de WorldPrefab.", this);
                return;
            }

            _ghostFootprint = prefab.TryGetComponent(out TrapBase prefabTrap) ? prefabTrap.FootprintRadius : 0.6f;
            _ghost = BuildGhost(prefab);
            _previewSlot = slotIndex;
        }

        private void EndPreview()
        {
            _previewSlot = -1;
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
        }

        private void UpdatePreview(Mouse mouse)
        {
            Inventory inv = playerInventory != null ? playerInventory.Inventory : null;
            if (inv == null || !IsTrap(inv.GetSlot(_previewSlot)))
            {
                EndPreview();
                return;
            }

            bool cancel = mouse.rightButton.wasPressedThisFrame
                          || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
            if (cancel) { EndPreview(); return; }

            _ghostValid = TryGetPlacement(out _ghostPosition)
                          && !TrapBase.IsOccupied(_ghostPosition, _ghostFootprint)
                          && !HitsObstacle(_ghostPosition, _ghostFootprint);
            _ghost.transform.SetPositionAndRotation(
                _ghostPosition, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            _ghostMaterial.color = _ghostValid ? validColor : invalidColor;
            if (_ghostMaterial.HasProperty("_BaseColor"))
                _ghostMaterial.SetColor("_BaseColor", _ghostMaterial.color);

            if (_ghostValid && mouse.middleButton.wasPressedThisFrame)
            {
                int slot = _previewSlot;
                EndPreview();
                PlaceFromSlot(slot, _ghostPosition);
            }
        }

        /// <summary>Point au sol visé par la caméra, limité à maxPlaceRange autour du joueur.</summary>
        private bool TryGetPlacement(out Vector3 position)
        {
            position = transform.position + transform.forward * placeDistance;

            Camera cam = Camera.main;
            Ray ray = cam != null
                ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : new Ray(transform.position + Vector3.up, transform.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
                return false;

            position = hit.point;
            Vector3 flat = hit.point - transform.position;
            flat.y = 0f;
            if (flat.magnitude > maxPlaceRange)
            {
                // Hors de portée : on ramène le fantôme au bord de la zone, en rouge.
                Vector3 clamped = transform.position + flat.normalized * maxPlaceRange;
                if (Physics.Raycast(clamped + Vector3.up * 50f, Vector3.down, out RaycastHit edge, 100f, groundMask,
                        QueryTriggerInteraction.Ignore))
                    position = edge.point;
                return false;
            }

            return Vector3.Angle(hit.normal, Vector3.up) <= maxSlope;
        }

        /// <summary>
        /// Vrai si un objet solide (décor, bâtiment, autre asset...) occupe l'emplacement.
        /// Le sol, le joueur et les ennemis sont ignorés.
        /// </summary>
        private bool HitsObstacle(Vector3 position, float radius)
        {
            float r = Mathf.Max(0.2f, radius * 0.6f);
            Vector3 center = position + Vector3.up * (r + 0.35f);
            Collider[] hits = Physics.OverlapSphere(center, r, groundMask, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits)
            {
                if (c is TerrainCollider) continue;
                if (c.transform.IsChildOf(transform)) continue;
                if (c.GetComponentInParent<Core.TowerDefense.EnemyController>() != null) continue;
                if (c.GetComponentInParent<TrapBase>() != null) return true;
                if (c.bounds.max.y <= position.y + 0.3f) continue; // dalle/sol plat
                return true;
            }
            return false;
        }

        private GameObject BuildGhost(GameObject prefab)
        {
            if (_ghostMaterial == null) _ghostMaterial = MakeGhostMaterial();

            // Instancié sous un parent inactif : aucun OnEnable/Awake du piège ne s'exécute.
            var holder = new GameObject("TrapGhostHolder");
            holder.SetActive(false);
            GameObject ghost = Instantiate(prefab, holder.transform);
            ghost.name = prefab.name + "_Ghost";

            foreach (MonoBehaviour mb in ghost.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) DestroyImmediate(mb);
            foreach (Collider c in ghost.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(c);
            foreach (Renderer r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMaterial;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            ghost.transform.SetParent(null, true);
            Destroy(holder);
            return ghost;
        }

        private static Material MakeGhostMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
            }
            mat.renderQueue = 3000;
            return mat;
        }

        private bool PlaceFromSlot(int slotIndex, Vector3 position)
        {
            Inventory inv = playerInventory != null ? playerInventory.Inventory : null;
            if (inv == null || slotIndex < 0 || slotIndex >= inv.Capacity) return false;

            ItemStack stack = inv.GetSlot(slotIndex);
            if (!IsTrap(stack) || stack.Definition.WorldPrefab == null) return false;

            int level = stack.UpgradeLevel;
            GameObject go = Instantiate(stack.Definition.WorldPrefab, position,
                Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            if (go.TryGetComponent(out TrapBase trap))
                trap.ApplyForgeUpgrade(forgeDefinition, level, gameObject);

            inv.RemoveAt(slotIndex, 1);
            return true;
        }

        #endregion
    }
}
