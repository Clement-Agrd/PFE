using System;
using Core;
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
            if (!IsPlacing) return;

            UpdatePreview();

            if (Input.GetKeyDown(cancelKey) || Input.GetMouseButtonDown(cancelMouseButton))
            {
                CancelPlacement();
                return;
            }

            if (_previewValid && Input.GetMouseButtonDown(confirmMouseButton))
                ConfirmPlacement();
        }

        public void Configure(Barracks owner, SOBarracks data)
        {
            barracks = owner;
            _data = data;
        }

        public void BeginPlacement(GameObject interactor)
        {
            if (barracks == null || _data == null) return;

            if (_activePlacement != null && _activePlacement != this)
                _activePlacement.CancelPlacement();

            _activePlacement = this;
            _interactor = interactor;

            ResolveCamera();

            if (placementCamera == null)
            {
                Debug.LogWarning("[BarracksFlagPlacement] Aucune caméra trouvée.", this);
                return;
            }

            EnsurePreview();
            IsPlacing = true;

            if (_previewInstance != null)
                _previewInstance.SetActive(true);

            GameFreeze.RequestCursorUnlock(this);
            UpdatePreview();
        }

        public void CancelPlacement()
        {
            IsPlacing = false;
            _previewValid = false;

            if (_previewInstance != null)
                _previewInstance.SetActive(false);

            GameFreeze.ReleaseCursorUnlock(this);
            _interactor = null;

            if (_activePlacement == this)
                _activePlacement = null;
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

        private void ResolveCamera()
        {
            if (placementCamera != null) return;

            if (_interactor != null)
                placementCamera = _interactor.GetComponentInChildren<Camera>();

            if (placementCamera == null)
                placementCamera = Camera.main;
        }

        private void UpdatePreview()
        {
            if (placementCamera == null || barracks == null || _data == null)
            {
                _previewValid = false;
                UpdatePreviewVisual();
                return;
            }

            Ray ray = placementCamera.ScreenPointToRay(Input.mousePosition);

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
                _previewValid = true;
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
            CancelPlacement();
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
