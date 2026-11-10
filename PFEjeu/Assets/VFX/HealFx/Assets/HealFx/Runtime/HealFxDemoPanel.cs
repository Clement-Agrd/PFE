using UnityEngine;
using UnityEngine.InputSystem;

namespace HealFx
{
    /// <summary>
    /// Petit panneau runtime (IMGUI) pour régler le FX en direct dans la Training Room.
    /// Choix IMGUI plutôt qu'UI Toolkit : aucun asset à câbler (ni PanelSettings, ni UXML), le panneau vit entièrement dans ce script,
    /// et c'est un outil de debug : les quelques allocations d'OnGUI sont sans importance ici (à ne pas copier pour une UI de jeu).
    /// Toutes les valeurs passent par HealZoneFx / HealTargetFx (donc par MaterialPropertyBlock) : aucun matériau instancié.
    ///
    /// Raccourcis : Espace = relancer le cycle, 1 / 2 / 3 = qualité Low / Medium / High, F1 = afficher / masquer le panneau.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealFxDemoPanel : MonoBehaviour
    {
        private const float PanelWidth = 300f;
        private const float Margin = 12f;

        [Header("Cibles du réglage")]
        [SerializeField] private HealZoneFx zone;
        [SerializeField] private HealFxLoopDemo loop;
        [SerializeField] private HealTargetFx[] targets;

        [Header("Palettes sélectionnables")]
        [SerializeField] private HealFxPalette[] palettes;

        [Header("Affichage")]
        [SerializeField] private bool visible = true;

        // Valeurs affichées (copiées une fois à l'activation, puis modifiées par les sliders).
        private float radius;
        private float intensity;
        private float speed;
        private float density;
        private float hue;
        private float playDuration;
        private float pauseDuration;
        private bool loopOn;
        private bool initialised;

        /// <summary>Utilisé par l'installeur Editor pour câbler le prefab.</summary>
        public void Bind(HealZoneFx healZone, HealFxLoopDemo loopDemo, HealTargetFx[] healTargets, HealFxPalette[] availablePalettes)
        {
            zone = healZone;
            loop = loopDemo;
            targets = healTargets;
            palettes = availablePalettes;
        }

        #region Cycle de vie Unity
        private void OnEnable()
        {
            initialised = false;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || zone == null) return;

            if (keyboard.spaceKey.wasPressedThisFrame && loop != null) loop.Restart();
            if (keyboard.digit1Key.wasPressedThisFrame) zone.SetQuality(HealFxQuality.Low);
            if (keyboard.digit2Key.wasPressedThisFrame) zone.SetQuality(HealFxQuality.Medium);
            if (keyboard.digit3Key.wasPressedThisFrame) zone.SetQuality(HealFxQuality.High);
            if (keyboard.f1Key.wasPressedThisFrame) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible || zone == null) return;
            if (!initialised) ReadCurrentValues();

            GUILayout.BeginArea(new Rect(Margin, Margin, PanelWidth, Screen.height - 2f * Margin), GUI.skin.box);
            GUILayout.Label("Heal FX : réglage en direct   (F1 masque)");

            DrawPhase();
            DrawLoopControls();
            DrawSliders();
            DrawQuality();
            DrawPalettes();

            GUILayout.EndArea();
        }
        #endregion

        #region Sections du panneau
        private void DrawPhase()
        {
            GUILayout.Label("Phase : " + zone.Phase);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Espace : relancer") && loop != null) loop.Restart();
            if (GUILayout.Button("Fin (dissipation)")) zone.Stop();
            GUILayout.EndHorizontal();
        }

        private void DrawLoopControls()
        {
            if (loop == null) return;

            bool newLoopOn = GUILayout.Toggle(loopOn, "Boucle automatique");
            if (newLoopOn != loopOn)
            {
                loopOn = newLoopOn;
                loop.SetLoopEnabled(loopOn);
            }

            float newPlay = Slider("Durée de la boucle (s)", playDuration, 1f, 20f);
            if (!Mathf.Approximately(newPlay, playDuration)) { playDuration = newPlay; loop.SetPlayDuration(playDuration); }

            float newPause = Slider("Pause entre cycles (s)", pauseDuration, 0f, 5f);
            if (!Mathf.Approximately(newPause, pauseDuration)) { pauseDuration = newPause; loop.SetPauseDuration(pauseDuration); }
        }

        private void DrawSliders()
        {
            float newRadius = Slider("Rayon (m)", radius, 1f, 8f);
            if (!Mathf.Approximately(newRadius, radius)) { radius = newRadius; zone.SetRadius(radius); }

            float newIntensity = Slider("Intensité (HDR)", intensity, 0f, 4f);
            if (!Mathf.Approximately(newIntensity, intensity))
            {
                intensity = newIntensity;
                zone.SetIntensity(intensity);
                if (targets != null)
                    for (int i = 0; i < targets.Length; i++)
                        if (targets[i] != null) targets[i].SetIntensity(intensity);
            }

            float newSpeed = Slider("Vitesse", speed, 0.1f, 3f);
            if (!Mathf.Approximately(newSpeed, speed)) { speed = newSpeed; zone.SetSpeed(speed); }

            float newDensity = Slider("Densité de particules", density, 0f, 2f);
            if (!Mathf.Approximately(newDensity, density)) { density = newDensity; zone.SetParticleDensity(density); }

            float newHue = Slider("Teinte (décalage)", hue, 0f, 1f);
            if (!Mathf.Approximately(newHue, hue)) { hue = newHue; zone.SetHueShift(hue); }
        }

        private void DrawQuality()
        {
            GUILayout.Label("Qualité : " + zone.Quality + "   (touches 1 / 2 / 3)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Low")) zone.SetQuality(HealFxQuality.Low);
            if (GUILayout.Button("Med")) zone.SetQuality(HealFxQuality.Medium);
            if (GUILayout.Button("High")) zone.SetQuality(HealFxQuality.High);
            GUILayout.EndHorizontal();
        }

        private void DrawPalettes()
        {
            if (palettes == null || palettes.Length == 0) return;

            GUILayout.Label("Palette");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < palettes.Length; i++)
            {
                if (palettes[i] == null) continue;
                if (!GUILayout.Button(palettes[i].name)) continue;

                zone.SetPalette(palettes[i]);
                if (targets == null) continue;
                for (int t = 0; t < targets.Length; t++)
                    if (targets[t] != null) targets[t].SetPalette(palettes[i]);
            }
            GUILayout.EndHorizontal();
        }
        #endregion

        #region Utilitaires
        private void ReadCurrentValues()
        {
            radius = zone.Radius;
            intensity = zone.Intensity;
            speed = zone.Speed;
            density = zone.ParticleDensity;
            hue = zone.HueShift;
            if (loop != null)
            {
                loopOn = loop.LoopEnabled;
                playDuration = loop.PlayDuration;
                pauseDuration = loop.PauseDuration;
            }
            initialised = true;
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));
            float result = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(result.ToString("0.00"), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            return result;
        }
        #endregion
    }
}
