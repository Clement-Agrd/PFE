using UnityEngine;
using UnityEngine.UI;

namespace ProfessionalTPS.UI
{
    public sealed class PlayerStaminaWorldUI :
        MonoBehaviour
    {
        [Header("References")]

        [SerializeField]
        private PlayerStamina stamina;

        [SerializeField]
        private Image staminaFill;

        [SerializeField]
        private CanvasGroup canvasGroup;

        [SerializeField]
        private Camera targetCamera;


        [Header("Visibility")]

        [Tooltip(
            "Cache le cercle lorsque la stamina est complètement remplie."
        )]
        [SerializeField]
        private bool hideWhenFull = true;

        [Tooltip(
            "Temps pendant lequel le cercle reste visible après avoir retrouvé toute sa stamina."
        )]
        [SerializeField, Min(0f)]
        private float hideDelay = 0.6f;

        [SerializeField, Min(0f)]
        private float fadeSpeed = 8f;


        private float _targetAlpha = 1f;

        private float _fullSince =
            -1f;


        // ============================================================
        // UNITY
        // ============================================================

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera =
                    Camera.main;
            }


            if (stamina == null)
            {
                stamina =
                    GetComponentInParent<
                        PlayerStamina
                    >();
            }


            if (canvasGroup == null)
            {
                canvasGroup =
                    GetComponent<CanvasGroup>();
            }
        }


        private void OnEnable()
        {
            if (stamina == null)
                return;


            stamina.OnStaminaChanged +=
                OnStaminaChanged;

            stamina.OnExhausted +=
                OnExhausted;
        }


        private void OnDisable()
        {
            if (stamina == null)
                return;


            stamina.OnStaminaChanged -=
                OnStaminaChanged;

            stamina.OnExhausted -=
                OnExhausted;
        }


        private void Start()
        {
            if (stamina == null)
                return;


            Refresh(
                stamina.CurrentStamina,
                stamina.MaxStamina
            );


            if (hideWhenFull &&
                stamina.IsFull)
            {
                _targetAlpha = 0f;

                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 0f;
                }
            }
        }


        private void Update()
        {
            UpdateVisibility();


            if (canvasGroup != null)
            {
                canvasGroup.alpha =
                    Mathf.MoveTowards(
                        canvasGroup.alpha,
                        _targetAlpha,
                        fadeSpeed *
                        Time.deltaTime
                    );
            }
        }


        private void LateUpdate()
        {
            FaceCamera();
        }


        // ============================================================
        // STAMINA
        // ============================================================

        private void OnStaminaChanged(
            float current,
            float max)
        {
            Refresh(
                current,
                max
            );


            // Dès qu'on consomme de la stamina,
            // l'indicateur réapparaît.
            if (current <
                max - 0.01f)
            {
                _fullSince = -1f;

                _targetAlpha = 1f;
            }
        }


        private void OnExhausted()
        {
            _fullSince = -1f;

            _targetAlpha = 1f;
        }


        private void Refresh(
            float current,
            float max)
        {
            if (staminaFill == null)
                return;


            float normalized =
                max > 0f
                    ? current / max
                    : 0f;


            staminaFill.fillAmount =
                Mathf.Clamp01(
                    normalized
                );
        }


        // ============================================================
        // VISIBILITY
        // ============================================================

        private void UpdateVisibility()
        {
            if (!hideWhenFull ||
                stamina == null)
            {
                _targetAlpha = 1f;

                return;
            }


            if (!stamina.IsFull)
            {
                _fullSince = -1f;

                _targetAlpha = 1f;

                return;
            }


            if (_fullSince < 0f)
            {
                _fullSince =
                    Time.time;
            }


            if (Time.time >=
                _fullSince +
                hideDelay)
            {
                _targetAlpha = 0f;
            }
        }


        // ============================================================
        // CAMERA
        // ============================================================

        private void FaceCamera()
        {
            if (targetCamera == null)
            {
                targetCamera =
                    Camera.main;
            }


            if (targetCamera == null)
                return;


            transform.rotation =
                targetCamera.transform.rotation;
        }
    }
}