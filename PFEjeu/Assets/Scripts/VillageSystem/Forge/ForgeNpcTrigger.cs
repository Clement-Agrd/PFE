using TMPro;
using UnityEngine;
using Core.Village.UI;

namespace Core.Village
{
    /// <summary>
    /// Le forgeron : approche-toi, appuie sur E, le panneau de la Forge s'ouvre
    /// (même pattern que TavernTrigger). Feedback visuel : un losange doré flotte
    /// toujours au-dessus du PNJ ; en entrant dans la zone, un anneau s'allume au sol
    /// et une étiquette résume ce qui est améliorable et ce qui est en vente
    /// (sans tenir compte des ressources possédées).
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class ForgeNpcTrigger : MonoBehaviour
    {
        [SerializeField] private ForgePanelUI panel;
        [SerializeField] private ForgeService forge;
        [SerializeField] private VillageInputReader input;
        [Tooltip("Texte affiché quand le joueur est dans la zone.")]
        [SerializeField] private GameObject promptUI;
        [SerializeField] private string promptMessage = "[E] Forgeron";

        [Header("Feedback visuel")]
        [SerializeField] private Color idleColor = new(0.95f, 0.78f, 0.35f, 0.12f);
        [SerializeField] private Color activeColor = new(0.95f, 0.78f, 0.35f, 0.55f);
        [SerializeField] private float markerHeight = 2.4f;
        [SerializeField] private float groundOffset = -0.95f;

        private bool _playerInRange;
        private TMP_Text _promptText;
        private SphereCollider _zone;

        private Transform _ring;
        private Renderer _ringRenderer;
        private Transform _marker;
        private Renderer _markerRenderer;
        private TextMeshPro _label;
        private float _nextLabelRefresh;

        private void Awake()
        {
            _zone = GetComponent<SphereCollider>();
            if (promptUI != null) _promptText = promptUI.GetComponentInChildren<TMP_Text>(true);
            BuildVisuals();
        }

        private void OnEnable()
        {
            if (input != null) input.InteractPressed += HandleInteractPressed;
        }

        private void OnDisable()
        {
            if (input != null) input.InteractPressed -= HandleInteractPressed;
            if (_playerInRange) HidePrompt();
        }

        private void Update()
        {
            UpdatePrompt();
            UpdateVisuals();
        }

        private void UpdatePrompt()
        {
            if (promptUI == null) return;

            bool show = _playerInRange && panel != null && !panel.IsOpen;
            if (show)
            {
                if (_promptText != null && _promptText.text != promptMessage) _promptText.text = promptMessage;
                if (!promptUI.activeSelf) promptUI.SetActive(true);
            }
            else if (_playerInRange && promptUI.activeSelf)
            {
                promptUI.SetActive(false);
            }
        }

        private void HandleInteractPressed()
        {
            if (!_playerInRange || panel == null || panel.IsOpen) return;
            panel.Open();
        }

        #region Feedback visuel

        private void BuildVisuals()
        {
            // Sprites/Default : non éclairé, gère la transparence, présent dans tous les builds.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;

            float radius = _zone.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);

            _ring = CreatePrimitive(PrimitiveType.Cylinder, "ForgeRing", shader, out _ringRenderer);
            _ring.localPosition = new Vector3(0f, groundOffset, 0f);
            _ring.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);

            _marker = CreatePrimitive(PrimitiveType.Sphere, "ForgeMarker", shader, out _markerRenderer);
            _marker.localPosition = new Vector3(0f, markerHeight, 0f);
            _marker.localScale = new Vector3(0.35f, 0.5f, 0.35f);

            var labelGo = new GameObject("ForgeLabel");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, markerHeight + 0.9f, 0f);
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 3.2f;
            _label.color = Color.white;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            labelGo.SetActive(false);
        }

        private Transform CreatePrimitive(PrimitiveType type, string name, Shader shader, out Renderer renderer)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            renderer = go.GetComponent<Renderer>();
            renderer.material = new Material(shader);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private void UpdateVisuals()
        {
            if (_marker == null) return;

            float t = Time.unscaledTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * (_playerInRange ? 6f : 2.5f));

            _marker.localPosition = new Vector3(0f, markerHeight + Mathf.Sin(t * 2f) * 0.12f, 0f);
            _marker.localRotation = Quaternion.Euler(0f, t * 90f, 0f);
            float scale = _playerInRange ? 1f + pulse * 0.35f : 1f;
            _marker.localScale = new Vector3(0.35f, 0.5f, 0.35f) * scale;
            _markerRenderer.material.color = _playerInRange ? Color.Lerp(activeColor, Color.white, pulse) : new Color(0.95f, 0.78f, 0.35f, 0.9f);

            Color ring = _playerInRange ? Color.Lerp(idleColor, activeColor, pulse) : idleColor;
            _ringRenderer.material.color = ring;

            if (_label == null) return;
            bool showLabel = _playerInRange && (panel == null || !panel.IsOpen);
            if (_label.gameObject.activeSelf != showLabel) _label.gameObject.SetActive(showLabel);
            if (!showLabel) return;

            Camera cam = Camera.main;
            if (cam != null) _label.transform.rotation = cam.transform.rotation;

            if (Time.unscaledTime >= _nextLabelRefresh)
            {
                _nextLabelRefresh = Time.unscaledTime + 0.25f;
                _label.text = BuildLabelText();
            }
        }

        private string BuildLabelText()
        {
            if (forge == null) return "<b>Forgeron</b>";
            return $"<b>Forgeron</b>\nObjets améliorables : <color=#F2C75A>{forge.CountUpgradableItems()}</color>\n" +
                   $"Articles en vente : <color=#F2C75A>{forge.ShopCount}</color>";
        }

        #endregion

        private void HidePrompt()
        {
            if (promptUI != null) promptUI.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player")) _playerInRange = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInRange = false;
            HidePrompt();
            _nextLabelRefresh = 0f;
        }
    }
}
