using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

using Core.DayNightSystem;
using Core.InteractionSystem;

namespace Core.FXZone
{
    /// <summary>
    /// Console de la zone FX : le joueur s'approche, appuie sur la touche
    /// d'interaction et un panneau s'ouvre pour régler heure, skybox,
    /// brouillard et post-process (Volume global) en direct.
    /// </summary>
    public sealed class FXControlConsole :
        InteractableBase
    {
        [Header("Cycle jour / nuit")]

        [SerializeField]
        private DayNightCycle dayNight;

        [Tooltip("Material de skybox utilisant Skybox/Blended (propriété _Blend).")]
        [SerializeField]
        private Material skyboxMaterial;


        [Header("Post-process")]

        [SerializeField]
        private Volume volume;


        private static readonly int BlendId =
            Shader.PropertyToID("_Blend");

        private bool _open;
        private bool _manualSky;
        private float _skyBlend;
        private Vector2 _scroll;

        private Bloom _bloom;
        private ColorAdjustments _color;
        private Vignette _vignette;
        private readonly List<VolumeComponent> _all = new();

        private bool _fogDefault;
        private Color _fogColorDefault;
        private float _fogDensityDefault;
        private FogMode _fogModeDefault;

        private GUIStyle _title;
        private GUIStyle _small;
        private Texture2D _background;


        public override string Prompt =>
            "Ouvrir la console FX";


        public override bool CanInteract(
            GameObject interactor)
        {
            return isActiveAndEnabled && !_open;
        }


        public override void Interact(
            GameObject interactor)
        {
            SetOpen(true);
        }


        private void Awake()
        {
            if (volume != null)
            {
                // profile (et non sharedProfile) : on ne modifie pas l'asset.
                VolumeProfile profile = volume.profile;

                profile.TryGet(out _bloom);
                profile.TryGet(out _color);
                profile.TryGet(out _vignette);

                _all.AddRange(profile.components);
            }

            _fogDefault = RenderSettings.fog;
            _fogColorDefault = RenderSettings.fogColor;
            _fogDensityDefault = RenderSettings.fogDensity;
            _fogModeDefault = RenderSettings.fogMode;
        }


        private void OnDisable()
        {
            if (_open)
                SetOpen(false);
        }


        private void Update()
        {
            if (_open &&
                Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetOpen(false);
            }
        }


        private void SetOpen(
            bool open)
        {
            if (_open == open)
                return;

            _open = open;

            if (open)
            {
                if (skyboxMaterial != null)
                    _skyBlend = skyboxMaterial.GetFloat(BlendId);

                GameFreeze.RequestPause(this);
                GameFreeze.RequestCursorUnlock(this);
            }
            else
            {
                GameFreeze.ReleasePause(this);
                GameFreeze.ReleaseCursorUnlock(this);
            }
        }


        // ============================================================
        // PANEL
        // ============================================================

        private void OnGUI()
        {
            if (!_open)
                return;

            BuildStyles();

            float scale = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float width = 560f;
            float height = 640f;

            Rect area = new Rect(
                (Screen.width / scale - width) * 0.5f,
                (720f - height) * 0.5f,
                width,
                height);

            GUI.DrawTexture(area, _background);

            GUILayout.BeginArea(new Rect(
                area.x + 16f, area.y + 12f,
                area.width - 32f, area.height - 24f));

            GUILayout.Label("CONSOLE FX", _title);
            GUILayout.Label("Échap pour fermer.", _small);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(520f));

            DrawTime();
            DrawSky();
            DrawFog();
            DrawPostProcess();

            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Réinitialiser post-process / brouillard", GUILayout.Height(30f)))
                ResetEffects();

            if (GUILayout.Button("Fermer", GUILayout.Height(30f), GUILayout.Width(120f)))
                SetOpen(false);

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }


        private void DrawTime()
        {
            Section("Heure");

            if (dayNight == null)
            {
                GUILayout.Label("DayNightCycle non assigné.", _small);
                return;
            }

            GUILayout.BeginHorizontal();
            PresetButton("Aube", 6f);
            PresetButton("Jour", 12f);
            PresetButton("Crépuscule", 18.5f);
            PresetButton("Nuit", 23f);
            GUILayout.EndHorizontal();

            float hour = dayNight.Hour;
            float newHour = Slider("Heure", hour, 0f, 24f, "0.0");

            if (!Mathf.Approximately(newHour, hour))
            {
                dayNight.SetTime(newHour);
                SyncSkyToCycle();
            }

            dayNight.Paused = GUILayout.Toggle(
                dayNight.Paused, " Figer le cycle");
        }


        private void PresetButton(
            string label,
            float hour)
        {
            if (!GUILayout.Button(label, GUILayout.Height(26f)))
                return;

            dayNight.SetTime(hour);
            SyncSkyToCycle();
        }


        // La skybox suit normalement le cycle avec un fondu en temps scalé,
        // figé tant que le jeu est en pause : on la recale donc à la main.
        private void SyncSkyToCycle()
        {
            _manualSky = false;

            if (skyboxMaterial != null)
            {
                _skyBlend = dayNight.NightBlend01;
                skyboxMaterial.SetFloat(BlendId, _skyBlend);
            }
        }


        private void DrawSky()
        {
            Section("Skybox");

            if (skyboxMaterial == null)
            {
                GUILayout.Label("Material de skybox non assigné.", _small);
                return;
            }

            float blend = Slider("Jour (0) / Nuit (1)", _skyBlend, 0f, 1f, "0.00");

            if (!Mathf.Approximately(blend, _skyBlend))
            {
                _skyBlend = blend;
                _manualSky = true;
                skyboxMaterial.SetFloat(BlendId, blend);
            }

            if (_manualSky)
                GUILayout.Label("Blend manuel : le cycle le reprendra au prochain changement d'heure.", _small);
        }


        private void DrawFog()
        {
            Section("Brouillard");

            RenderSettings.fog = GUILayout.Toggle(RenderSettings.fog, " Activé");

            if (!RenderSettings.fog)
                return;

            RenderSettings.fogDensity =
                Slider("Densité", RenderSettings.fogDensity, 0f, 0.1f, "0.000");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Brume claire")) RenderSettings.fogColor = new Color(0.7f, 0.75f, 0.8f);
            if (GUILayout.Button("Brume nuit")) RenderSettings.fogColor = new Color(0.02f, 0.03f, 0.08f);
            if (GUILayout.Button("Brume rouge")) RenderSettings.fogColor = new Color(0.35f, 0.08f, 0.05f);
            GUILayout.EndHorizontal();
        }


        private void DrawPostProcess()
        {
            Section("Post-process");

            if (volume == null)
            {
                GUILayout.Label("Volume non assigné.", _small);
                return;
            }

            volume.enabled = GUILayout.Toggle(volume.enabled, " Volume global activé");
            volume.weight = Slider("Poids du volume", volume.weight, 0f, 1f, "0.00");

            if (_bloom != null)
            {
                _bloom.active = GUILayout.Toggle(_bloom.active, " Bloom");
                if (_bloom.active)
                {
                    _bloom.intensity.Override(
                        Slider("   Intensité", _bloom.intensity.value, 0f, 10f, "0.00"));
                    _bloom.threshold.Override(
                        Slider("   Seuil", _bloom.threshold.value, 0f, 3f, "0.00"));
                }
            }

            if (_color != null)
            {
                _color.active = GUILayout.Toggle(_color.active, " Color Adjustments");
                if (_color.active)
                {
                    _color.postExposure.Override(
                        Slider("   Exposition", _color.postExposure.value, -5f, 5f, "0.00"));
                    _color.contrast.Override(
                        Slider("   Contraste", _color.contrast.value, -100f, 100f, "0"));
                    _color.saturation.Override(
                        Slider("   Saturation", _color.saturation.value, -100f, 100f, "0"));
                }
            }

            if (_vignette != null)
            {
                _vignette.active = GUILayout.Toggle(_vignette.active, " Vignette");
                if (_vignette.active)
                {
                    _vignette.intensity.Override(
                        Slider("   Intensité", _vignette.intensity.value, 0f, 1f, "0.00"));
                }
            }

            // Les autres effets du profil : simple interrupteur.
            for (int i = 0; i < _all.Count; i++)
            {
                VolumeComponent c = _all[i];

                if (c == null || c == _bloom || c == _color || c == _vignette)
                    continue;

                c.active = GUILayout.Toggle(c.active, " " + c.GetType().Name);
            }
        }


        private void ResetEffects()
        {
            if (volume != null)
            {
                volume.enabled = true;
                volume.weight = 1f;

                for (int i = 0; i < _all.Count; i++)
                {
                    if (_all[i] != null)
                        _all[i].active = true;
                }
            }

            RenderSettings.fog = _fogDefault;
            RenderSettings.fogColor = _fogColorDefault;
            RenderSettings.fogDensity = _fogDensityDefault;
            RenderSettings.fogMode = _fogModeDefault;
        }


        // ============================================================
        // HELPERS
        // ============================================================

        private void Section(
            string title)
        {
            GUILayout.Space(10f);
            GUILayout.Label(title.ToUpperInvariant(), _title);
        }


        private static float Slider(
            string label,
            float value,
            float min,
            float max,
            string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(170f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(250f));
            GUILayout.Label(value.ToString(format), GUILayout.Width(50f));
            GUILayout.EndHorizontal();

            return value;
        }


        private void BuildStyles()
        {
            if (_title != null && _background != null)
                return;

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            _small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };

            _background = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _background.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.12f, 0.96f));
            _background.Apply();
        }
    }
}
