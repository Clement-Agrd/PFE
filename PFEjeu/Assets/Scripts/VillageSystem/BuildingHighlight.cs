using UnityEngine;
using Core.Village;

namespace Core.Village
{
    /// <summary>
    /// Fait ressortir un bâtiment en vue village (surbrillance pulsée). Ne
    /// modifie aucun shader : teinte le _BaseColor existant via un
    /// MaterialPropertyBlock. Cherche les Renderer À LA DEMANDE (pas au
    /// démarrage) pour fonctionner même si le modèle visuel du Building est
    /// instancié dynamiquement après coup.
    /// </summary>
    [RequireComponent(typeof(Building))]
    public sealed class BuildingHighlight : MonoBehaviour
    {
        [SerializeField] private VillageViewController villageView;
        [Tooltip("Vide = détecte automatiquement tous les Renderer des enfants (recherché à chaque Show, pour capter un modèle instancié dynamiquement).")]
        [SerializeField] private Renderer[] renderers;

        [Header("Surbrillance")]
        [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.4f);
        [SerializeField, Range(0f, 1f)] private float highlightStrength = 0.5f;
        [SerializeField] private float pulseSpeed = 2f;
        [SerializeField, Range(0f, 0.5f)] private float pulseAmplitude = 0.15f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _block;
        private Color[] _originalColors;
        private bool _isHighlighted;
        private bool _manualRenderers;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _manualRenderers = renderers != null && renderers.Length > 0;
        }

        private void OnEnable()
        {
            if (villageView != null)
            {
                villageView.OnEnteredVillageView += Show;
                villageView.OnExitedVillageView += Hide;
            }
        }

        private void OnDisable()
        {
            if (villageView != null)
            {
                villageView.OnEnteredVillageView -= Show;
                villageView.OnExitedVillageView -= Hide;
            }
            Hide();
        }

        private void Update()
        {
            if (!_isHighlighted || pulseSpeed <= 0f) return;
            ApplyColor(highlightStrength + Mathf.Sin(Time.time * pulseSpeed) * pulseAmplitude);
        }

        public void Show()
        {
            RefreshRenderers();
            _isHighlighted = true;
            ApplyColor(highlightStrength);
        }

        public void Hide()
        {
            _isHighlighted = false;
            if (renderers == null) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                renderers[i].GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, _originalColors[i]);
                renderers[i].SetPropertyBlock(_block);
            }
        }

        // Recherche les Renderer et mémorise leur couleur d'origine, à chaque
        // fois qu'on entre en vue village (capte le modèle instancié depuis Building).
        private void RefreshRenderers()
        {
            if (_manualRenderers) return; // liste fournie à la main : on ne la touche pas

            renderers = GetComponentsInChildren<Renderer>();
            _originalColors = new Color[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].sharedMaterial != null && renderers[i].sharedMaterial.HasProperty(BaseColorId))
                    _originalColors[i] = renderers[i].sharedMaterial.GetColor(BaseColorId);
                else
                    _originalColors[i] = Color.white;
            }
        }

        private void ApplyColor(float strength)
        {
            if (renderers == null) return;
            strength = Mathf.Clamp01(strength);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                Color blended = Color.Lerp(_originalColors[i], highlightColor, strength);

                renderers[i].GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, blended);
                renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}