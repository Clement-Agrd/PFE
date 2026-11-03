using System;
using UnityEngine;
using UnityEngine.AI;

namespace Core.BarracksSystem
{
    [DisallowMultipleComponent]
    public sealed class BarracksFlagPlacement : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Barracks barracks;
        [SerializeField] private Camera placementCamera;

        [Header("Ground")]
        [Tooltip("Par défaut : tout sauf Player et Ignore Raycast.")]
        [SerializeField] private LayerMask groundMask = ~((1 << 6) | (1 << 2));
        [SerializeField, Min(0.1f)] private float maxRayDistance = 250f;
        [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 2f;

        [Header("Controls")]
        [SerializeField] private int confirmMouseButton = 0;
        [SerializeField] private int cancelMouseButton = 1;
        [SerializeField] private KeyCode cancelKey = KeyCode.Escape;

        [Header("Preview")]
        [SerializeField] private Color validColor = new Color(0.25f, 1f, 0.3f, 0.8f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.2f, 0.2f, 0.8f);

        private static BarracksFlagPlacement _activePlacement;
        private readonly RaycastHit[] _rayHits = new RaycastHit[32];

        private GameObject _previewInstance;
        private GameObject _flagInstance;
        private GameObject _interactor;
        private SOBarracks _data;
        private Vector3 _previewPosition;
        private bool _previewValid;

        private Core.Village.VillageViewController _mapView;
        private bool _automaticOnCancel;
        private int _beginFrame;
        private LineRenderer _rangeRing;
        private Material _rangeMaterial;
        public bool IsConstructionPlacement => IsPlacing && _automaticOnCancel;
        public bool IsPlacing { get; private set; }
        public bool HasFlag { get; private set; }
        public Vector3 FlagPosition { get; private set; }

        public event Action<Vector3> FlagPlaced;

        private void Awake()
        {
            if (barracks == null)
                barracks = GetComponent<Barracks>();
        }

        private void OnDisable()
        {
            if (IsPlacing)
                CancelPlacement();
        }

        private void Update()
        {
            if (!IsPlacing || Time.frameCount == _beginFrame) return;

            if (!IsInteractorStillInRange())
            {
                CancelPlacement();
                return;
            }

            UpdatePreview();

            if (Input.GetKeyDown(cancelKey) || Input.GetMouseButtonDown(cancelMouseButton))
            {
                CancelPlacement();
                return;
            }

            if (_mapView == null && Input.GetMouseButtonDown(confirmMouseButton) &&
                (UnityEngine.EventSystems.EventSystem.current == null || !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
                TryPlaceFromRay(PlacementRay());
        }

        public void Configure(Barracks owner, SOBarracks data)
        {
            barracks = owner;
            _data = data;
        }

        public void BeginPlacement(GameObject interactor) => BeginPlacement(interactor, null, false);

        public void BeginConstructionPlacement(GameObject interactor, Core.Village.VillageViewController view)
            => BeginPlacement(interactor, view != null && view.IsInVillageView ? view : null, true);

        private void BeginPlacement(GameObject interactor, Core.Village.VillageViewController view, bool automaticOnCancel)
        {
            if (barracks == null || _data == null) return;
            if (_activePlacement != null) _activePlacement.CancelPlacement();
            _interactor = interactor;
            _mapView = view;
            _automaticOnCancel = automaticOnCancel;
            if (_mapView != null)
            {
                placementCamera = _mapView.MapCamera;
                _mapView.OnMapAreaChanged += CancelPlacement;
                _mapView.OnBuildingClicked += BuildingSelected;
            }
            else ResolveCamera();
            if (placementCamera == null)
            {
                if (automaticOnCancel) PlaceAutomaticFlag();
                EndPlacement();
                return;
            }
            _activePlacement = this;
            IsPlacing = true;
            _beginFrame = Time.frameCount;
            // Give soldiers a valid provisional destination before Barracks.Start.
            if (automaticOnCancel) PlaceAutomaticFlag();
            if (_flagInstance != null) _flagInstance.SetActive(false);
            EnsurePreview();
            _previewInstance.SetActive(true);
            ShowRange();
            UpdatePreview();
        }

        private void BuildingSelected(Core.Village.Building building)
        {
            if (building != null && building.gameObject != gameObject) CancelPlacement();
        }

        public static void CancelActivePlacement()
        {
            if (_activePlacement != null) _activePlacement.CancelPlacement();
        }

        // The map handles confirmation once, using the same ray as building selection.
        // Returning false lets a different building/empty slot receive its normal click.
        public static bool TryHandleMapRay(Core.Village.VillageViewController view, Ray ray)
        {
            var active = _activePlacement;
            if (active == null || !active.IsPlacing || active._mapView != view) return false;
            if (Time.frameCount == active._beginFrame) return true;
            return active.TryPlaceFromRay(ray);
        }

        public bool TryPlaceFromRay(Ray ray)
        {
            if (!IsPlacing) return false;
            if (Physics.Raycast(ray, out var hit, maxRayDistance, groundMask, QueryTriggerInteraction.Ignore))
            {
                var building = hit.collider.GetComponentInParent<Core.Village.Building>();
                var slot = hit.collider.GetComponentInParent<Core.TowerDefense.DefenseBuildSlot>();
                if ((building != null && building.gameObject != gameObject) || (slot != null && !slot.IsOccupied))
                {
                    CancelPlacement();
                    return false;
                }
            }
            UpdatePreview(ray);
            if (_previewValid) ConfirmPlacement();
            else if (_automaticOnCancel) CancelPlacement();
            return true;
        }

        public void CancelPlacement()
        {
            if (IsPlacing && _automaticOnCancel) PlaceAutomaticFlag();
            EndPlacement();
        }

        private void EndPlacement()
        {
            if (_mapView != null)
            {
                _mapView.OnMapAreaChanged -= CancelPlacement;
                _mapView.OnBuildingClicked -= BuildingSelected;
            }
            _mapView = null;
            _automaticOnCancel = false;
            IsPlacing = false;
            _previewValid = false;
            if (_previewInstance != null) _previewInstance.SetActive(false);
            if (_flagInstance != null) _flagInstance.SetActive(true);
            if (_rangeRing != null) _rangeRing.gameObject.SetActive(false);
            _interactor = null;
            placementCamera = null;
            if (_activePlacement == this) _activePlacement = null;
        }

        public bool TryGetAutomaticFlagPosition(out Vector3 position)
        {
            position = default;
            if (barracks == null || _data == null) return false;
            float best = float.PositiveInfinity;
            Vector3 center = barracks.transform.position;
            foreach (var path in FindObjectsByType<Core.TowerDefense.EnemySplinePath>(FindObjectsSortMode.None))
            {
                if (!path.isActiveAndEnabled || path.gameObject.scene != gameObject.scene || !path.IsValid) continue;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    var a = path.GetPoint(i); var b = path.GetPoint(i + 1);
                    var direction = b - a; direction.y = 0;
                    var delta = center - a; delta.y = 0;
                    float t = direction.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(delta, direction) / direction.sqrMagnitude) : 0;
                    var point = Vector3.Lerp(a, b, t);
                    if (!NavMesh.SamplePosition(point, out var nav, navMeshSampleRadius, NavMesh.AllAreas)) continue;
                    float distance = PlanarDistanceSquared(nav.position, center);
                    if (distance > _data.FlagPlacementRange * _data.FlagPlacementRange || distance >= best) continue;
                    best = distance;
                    position = nav.position;
                }
            }
            return !float.IsPositiveInfinity(best);
        }

        private void PlaceAutomaticFlag()
        {
            if (TryGetAutomaticFlagPosition(out var point)) SetFlagPosition(point, true);
            else
            {
                // Legacy scenes may not contain an enemy spline within deployment range.
                SetInitialFlagPosition(transform.TransformPoint(_data.DefaultFlagOffset));
                FlagPlaced?.Invoke(FlagPosition);
            }
        }

        private static float PlanarDistanceSquared(Vector3 a, Vector3 b)
        {
            var delta = a - b; delta.y = 0; return delta.sqrMagnitude;
        }

        private void ShowRange()
        {
            if (_rangeRing == null)
            {
                var go = new GameObject("TroopPlacementRange"); go.transform.SetParent(transform, false); go.layer = 2;
                _rangeRing = go.AddComponent<LineRenderer>();
                _rangeRing.useWorldSpace = true; _rangeRing.loop = true; _rangeRing.positionCount = 128;
                _rangeRing.widthMultiplier = .22f; _rangeRing.numCornerVertices = 2;
                _rangeRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _rangeRing.receiveShadows = false;
                _rangeMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _rangeMaterial.SetColor("_BaseColor", new Color(1f, .8f, .15f));
                _rangeRing.sharedMaterial = _rangeMaterial;
            }
            _rangeRing.gameObject.SetActive(true);
            var center = barracks.transform.position;
            for (int i = 0; i < _rangeRing.positionCount; i++)
            {
                float angle = 2 * Mathf.PI * i / _rangeRing.positionCount;
                var point = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * _data.FlagPlacementRange;
                // Sample terrain only, so roofs, units and the preview do not deform the circle.
                foreach (var terrain in Terrain.activeTerrains)
                {
                    var local = point - terrain.transform.position; var size = terrain.terrainData.size;
                    if (local.x >= 0 && local.z >= 0 && local.x <= size.x && local.z <= size.z)
                    { point.y = terrain.SampleHeight(point) + terrain.transform.position.y; break; }
                }
                _rangeRing.SetPosition(i, point + Vector3.up * .18f);
            }
        }

        private void OnDestroy()
        {
            EndPlacement();
            if (_previewInstance != null) Destroy(_previewInstance);
            if (_flagInstance != null) Destroy(_flagInstance);
            if (_rangeMaterial != null) Destroy(_rangeMaterial);
        }

        public void SetInitialFlagPosition(Vector3 desiredWorldPosition)
        {
            if (_data == null || barracks == null) return;

            Vector3 clamped = ClampToRange(
                desiredWorldPosition,
                barracks.transform.position,
                _data.FlagPlacementRange);

            if (NavMesh.SamplePosition(
                    clamped,
                    out NavMeshHit hit,
                    navMeshSampleRadius,
                    NavMesh.AllAreas))
            {
                SetFlagPosition(hit.position, false);
            }
            else
            {
                SetFlagPosition(clamped, false);
            }
        }

        private bool IsInteractorStillInRange()
        {
            if (_mapView != null && _mapView.IsInVillageView) return true;
            if (_interactor == null || barracks == null || _data == null)
                return false;

            Vector3 playerPosition = _interactor.transform.position;
            Vector3 barracksPosition = barracks.transform.position;

            playerPosition.y = 0f;
            barracksPosition.y = 0f;

            // Après le début de l'interaction, on autorise le déplacement
            // dans toute la portée de placement de la caserne.
            // La petite portée de l'Interactor sert seulement à démarrer.
            float range = Mathf.Max(0.1f, _data.FlagPlacementRange);

            return (playerPosition - barracksPosition).sqrMagnitude <=
                   range * range;
        }


        private void ResolveCamera()
        {
            if (placementCamera != null) return;

            if (_interactor != null)
                placementCamera = _interactor.GetComponentInChildren<Camera>();

            if (placementCamera == null)
                placementCamera = Camera.main;
        }

        private Ray PlacementRay()
        {
            Vector2 screenPoint = _mapView != null ? (Vector2)Input.mousePosition :
                new Vector2(placementCamera.pixelWidth * .5f, placementCamera.pixelHeight * .5f);
            return placementCamera.ScreenPointToRay(screenPoint);
        }

        private void UpdatePreview()
        {
            if (placementCamera == null) return;
            UpdatePreview(PlacementRay());
        }

        private void UpdatePreview(Ray ray)
        {
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                _rayHits,
                maxRayDistance,
                groundMask,
                QueryTriggerInteraction.Ignore);

            bool found = false;
            float nearest = float.PositiveInfinity;
            Vector3 groundPoint = Vector3.zero;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _rayHits[i];
                if (hit.collider == null) continue;

                Transform hitTransform = hit.transform;

                if (_interactor != null &&
                    (hitTransform == _interactor.transform ||
                     hitTransform.IsChildOf(_interactor.transform)))
                {
                    continue;
                }

                if (hitTransform == barracks.transform ||
                    hitTransform.IsChildOf(barracks.transform))
                {
                    continue;
                }

                if (hit.distance >= nearest) continue;

                nearest = hit.distance;
                groundPoint = hit.point;
                found = true;
            }

            if (!found)
            {
                _previewValid = false;
                UpdatePreviewVisual();
                return;
            }

            Vector3 clamped = ClampToRange(
                groundPoint,
                barracks.transform.position,
                _data.FlagPlacementRange);

            if (NavMesh.SamplePosition(
                    clamped,
                    out NavMeshHit navHit,
                    navMeshSampleRadius,
                    NavMesh.AllAreas))
            {
                _previewPosition = navHit.position;
                _previewValid = !_automaticOnCancel ||
                    (PlanarDistanceSquared(groundPoint, barracks.transform.position) <= _data.FlagPlacementRange * _data.FlagPlacementRange &&
                     PlanarDistanceSquared(navHit.position, barracks.transform.position) <= _data.FlagPlacementRange * _data.FlagPlacementRange);
            }
            else
            {
                _previewPosition = clamped;
                _previewValid = false;
            }

            UpdatePreviewVisual();
        }

        private void ConfirmPlacement()
        {
            if (!_previewValid) return;

            SetFlagPosition(_previewPosition, true);
            EndPlacement();
        }

        private void SetFlagPosition(Vector3 worldPosition, bool notify)
        {
            FlagPosition = worldPosition;
            HasFlag = true;

            EnsureFinalFlag();

            if (_flagInstance != null)
                _flagInstance.transform.position = worldPosition;

            if (notify)
                FlagPlaced?.Invoke(worldPosition);
        }

        private void EnsurePreview()
        {
            if (_previewInstance != null) return;

            _previewInstance =
                _data != null && _data.FlagPreviewPrefab != null
                    ? Instantiate(_data.FlagPreviewPrefab)
                    : CreateFallbackFlag("BarracksFlagPreview", true);

            SetLayerRecursively(_previewInstance, 2);
        }

        private void EnsureFinalFlag()
        {
            if (_flagInstance != null) return;

            _flagInstance =
                _data != null && _data.FlagPrefab != null
                    ? Instantiate(_data.FlagPrefab)
                    : CreateFallbackFlag("BarracksFlag", false);

            SetLayerRecursively(_flagInstance, 2);
        }

        private void UpdatePreviewVisual()
        {
            if (_previewInstance == null) return;

            _previewInstance.transform.position = _previewPosition;

            Color color = _previewValid ? validColor : invalidColor;
            Renderer[] renderers = _previewInstance.GetComponentsInChildren<Renderer>(true);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);

            for (int i = 0; i < renderers.Length; i++)
                renderers[i].SetPropertyBlock(block);
        }

        private static Vector3 ClampToRange(Vector3 point, Vector3 center, float range)
        {
            Vector3 planar = point - center;
            planar.y = 0f;

            float safeRange = Mathf.Max(0.1f, range);

            if (planar.sqrMagnitude > safeRange * safeRange)
                planar = planar.normalized * safeRange;

            return new Vector3(
                center.x + planar.x,
                point.y,
                center.z + planar.z);
        }

        private static GameObject CreateFallbackFlag(string objectName, bool preview)
        {
            GameObject root = new GameObject(objectName);

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            pole.transform.localScale = new Vector3(0.04f, 0.75f, 0.04f);
            DestroyCollider(pole);

            GameObject cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cloth.name = "Flag";
            cloth.transform.SetParent(root.transform, false);
            cloth.transform.localPosition = new Vector3(0.32f, 1.3f, 0f);
            cloth.transform.localScale = new Vector3(0.6f, 0.35f, preview ? 0.035f : 0.05f);
            DestroyCollider(cloth);

            return root;
        }

        private static void DestroyCollider(GameObject target)
        {
            Collider collider = target.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;

            root.layer = layer;

            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }
    }
}
