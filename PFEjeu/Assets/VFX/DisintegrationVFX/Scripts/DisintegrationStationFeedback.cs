using TMPro;
using UnityEngine;
using Core.InteractionSystem;

namespace Core.DisintegrationVFX
{
    /// <summary>
    /// Feedback visuel de la station : un texte flottant (discret de loin, "[E] ..." à portée)
    /// et un pad qui s'illumine / pulse quand le joueur peut interagir.
    /// </summary>
    [RequireComponent(typeof(DisintegrationStation))]
    public sealed class DisintegrationStationFeedback : MonoBehaviour
    {
        [SerializeField] private Renderer pad;
        [SerializeField] private KeyCode prevStyleKey = KeyCode.LeftArrow;
        [SerializeField] private KeyCode nextStyleKey = KeyCode.RightArrow;
        [SerializeField] private float textHeight = 2.2f;
        [SerializeField] private float visibleDistance = 14f;
        [SerializeField] private Color idleColor = new Color(0.35f, 0.9f, 1f);
        [SerializeField] private Color readyColor = new Color(1f, 0.6f, 0.15f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmisId = Shader.PropertyToID("_EmissionColor");

        private DisintegrationStation _station;
        private Interactor _interactor;
        private TextMeshPro _label;
        private MaterialPropertyBlock _mpb;
        private Camera _cam;

        private void Awake()
        {
            _station = GetComponent<DisintegrationStation>();
            _mpb = new MaterialPropertyBlock();

            if (pad == null && transform.parent != null)
            {
                Transform p = transform.parent.Find("Pad");
                if (p != null) pad = p.GetComponent<Renderer>();
            }

            if (pad != null)
                pad.material.EnableKeyword("_EMISSION");

            var go = new GameObject("Prompt");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * textHeight;
            _label = go.AddComponent<TextMeshPro>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 3f;
            _label.fontStyle = FontStyles.Bold;
            _label.outlineWidth = 0.2f;
            _label.outlineColor = Color.black;
            _label.rectTransform.sizeDelta = new Vector2(8f, 2f);
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            if (_interactor == null) _interactor = FindAnyObjectByType<Interactor>();

            Vector3 camPos = _cam.transform.position;
            float dist = Vector3.Distance(camPos, transform.position);
            bool ready = _interactor != null && ReferenceEquals(_interactor.Current, _station);

            _label.gameObject.SetActive(dist < visibleDistance);
            _label.transform.rotation = _cam.transform.rotation;

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (ready ? 6f : 2f));
            if (ready)
            {
                if (Input.GetKeyDown(nextStyleKey)) _station.SelectStyle(1);
                if (Input.GetKeyDown(prevStyleKey)) _station.SelectStyle(-1);
                _label.text = "[" + _interactor.InteractKey + "]  " + _station.Prompt
                    + "\n<size=70%>« Style : " + _station.StyleName + " »  (flèches gauche/droite)</size>";
                _label.color = Color.Lerp(Color.white, readyColor, pulse);
                _label.transform.localScale = Vector3.one * (1f + 0.08f * pulse);
            }
            else
            {
                _label.text = "Station FX";
                _label.color = new Color(idleColor.r, idleColor.g, idleColor.b, 0.7f);
                _label.transform.localScale = Vector3.one * 0.7f;
            }

            if (pad != null)
            {
                Color c = ready ? readyColor : idleColor;
                float glow = ready ? 1.5f + 2.5f * pulse : 0.4f + 0.4f * pulse;
                pad.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                _mpb.SetColor(EmisId, c * glow);
                pad.SetPropertyBlock(_mpb);
            }
        }
    }
}
