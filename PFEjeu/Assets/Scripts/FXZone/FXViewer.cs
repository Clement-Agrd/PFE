using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.FXZone
{
    /// <summary>
    /// Mode "visionneuse FX" : la caméra principale quitte le joueur, qui est figé et masqué, et vole
    /// d'une baie FX (Zone_*) à l'autre via leurs repères Cam_Clip. Clic droit = orbite, molette = zoom.
    /// À poser sur ZONE_07_FX. Touche F9 pour entrer / sortir.
    /// </summary>
    public sealed class FXViewer : MonoBehaviour
    {
        [Header("Entrée")]
        [SerializeField] private Key toggleKey = Key.F9;

        [Header("Caméra")]
        [SerializeField] private float moveSmooth = 7f;
        [SerializeField] private float orbitSpeed = 0.15f;
        [SerializeField] private float zoomSpeed = 0.01f;
        [SerializeField] private Vector2 distanceRange = new Vector2(1.5f, 9f);
        [SerializeField] private float yawLimit = 85f;
        [SerializeField] private Vector2 pitchRange = new Vector2(-5f, 75f);
        [Tooltip("Hauteur (depuis le sol de la baie) du point regardé.")]
        [SerializeField] private float focusHeight = 3f;
        [Tooltip("Demi-largeur / demi-profondeur de la baie où la caméra est bridée (évite de passer dans les murs).")]
        [SerializeField] private Vector2 bayHalfExtent = new Vector2(5.6f, 9.3f);

        [Header("Affichage")]
        [SerializeField] private bool hideHud = true;

        private sealed class Bay
        {
            public Transform zone;
            public Transform camMarker;
        }

        private readonly List<Bay> bays = new List<Bay>();
        private int index;
        private bool active;
        private bool showHelp = true;

        private Camera cam;
        private Vector3 savedLocalPos;
        private Quaternion savedLocalRot;
        private readonly List<Behaviour> disabledBehaviours = new List<Behaviour>();
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        private float yaw, pitch, baseYaw, dist;
        private Vector3 focus;

        public bool IsActive => active;
        public int BayCount { get { if (bays.Count == 0) Collect(); return bays.Count; } }
        public string BayName(int i) => bays[i].zone.name.Replace("Zone_", "");

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb[toggleKey].wasPressedThisFrame)
            {
                if (active) Exit(); else Enter();
            }
            if (!active) return;

            if (kb.eKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) Select(index + 1);
            if (kb.qKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) Select(index - 1);
            for (int i = 0; i < 9 && i < bays.Count; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame) Select(i);
            if (kb.rKey.wasPressedThisFrame) ResetView();
            if (kb.hKey.wasPressedThisFrame) showHelp = !showHelp;

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * orbitSpeed;
                    pitch -= d.y * orbitSpeed;
                }
                dist -= mouse.scroll.ReadValue().y * zoomSpeed;
            }

            yaw = Mathf.Clamp(yaw, baseYaw - yawLimit, baseYaw + yawLimit);
            pitch = Mathf.Clamp(pitch, pitchRange.x, pitchRange.y);
            dist = Mathf.Clamp(dist, distanceRange.x, distanceRange.y);
        }

        private void LateUpdate()
        {
            if (!active || cam == null || bays.Count == 0) return;

            Vector3 zoneCenter = bays[index].zone.position;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = focus - rot * Vector3.forward * dist;
            pos.x = Mathf.Clamp(pos.x, zoneCenter.x - bayHalfExtent.x, zoneCenter.x + bayHalfExtent.x);
            pos.z = Mathf.Clamp(pos.z, zoneCenter.z - bayHalfExtent.y, zoneCenter.z + bayHalfExtent.y);
            pos.y = Mathf.Clamp(pos.y, 0.3f, 9.5f);

            Quaternion look = Quaternion.LookRotation(focus - pos, Vector3.up);
            float t = 1f - Mathf.Exp(-moveSmooth * Time.unscaledDeltaTime);
            cam.transform.SetPositionAndRotation(
                Vector3.Lerp(cam.transform.position, pos, t),
                Quaternion.Slerp(cam.transform.rotation, look, t));
        }

        /// <summary>Entre dans le mode visionneuse : fige et masque le joueur, prend la caméra principale.</summary>
        public void Enter()
        {
            if (active) return;
            Collect();
            if (bays.Count == 0)
            {
                Debug.LogWarning("[FXViewer] Aucune baie avec Cam_Clip trouvée.");
                return;
            }
            cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[FXViewer] Pas de caméra principale.");
                return;
            }

            // Joueur : composants désactivés et rendu masqué.
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                foreach (Behaviour b in player.GetComponents<Behaviour>())
                    if (b != null && b.enabled) { b.enabled = false; disabledBehaviours.Add(b); }
                foreach (Renderer r in player.GetComponentsInChildren<Renderer>(false))
                    if (r != null && r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
            }

            // Caméra : on coupe les scripts de suivi (Cinemachine, shakes...) et on la pilote nous-mêmes.
            savedLocalPos = cam.transform.localPosition;
            savedLocalRot = cam.transform.localRotation;
            foreach (Behaviour b in cam.GetComponents<Behaviour>())
            {
                if (b == null || !b.enabled) continue;
                if (b is Camera || b is AudioListener) continue;
                if (b.GetType().Name == "UniversalAdditionalCameraData") continue;
                b.enabled = false;
                disabledBehaviours.Add(b);
            }

            if (hideHud)
            {
                foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                    if (c.isRootCanvas && c.enabled && c.renderMode != RenderMode.WorldSpace)
                    { c.enabled = false; hiddenCanvases.Add(c); }
            }

            active = true;
            index = Mathf.Clamp(index, 0, bays.Count - 1);
            ResetView();
            cam.transform.SetPositionAndRotation(bays[index].camMarker.position, bays[index].camMarker.rotation);
        }

        /// <summary>Quitte le mode et rend tout au joueur.</summary>
        public void Exit()
        {
            if (!active) return;
            active = false;

            foreach (Behaviour b in disabledBehaviours) if (b != null) b.enabled = true;
            foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
            foreach (Canvas c in hiddenCanvases) if (c != null) c.enabled = true;
            disabledBehaviours.Clear();
            hiddenRenderers.Clear();
            hiddenCanvases.Clear();

            if (cam != null)
                cam.transform.SetLocalPositionAndRotation(savedLocalPos, savedLocalRot);
        }

        public void Select(int i)
        {
            if (bays.Count == 0) return;
            index = (i % bays.Count + bays.Count) % bays.Count;
            ResetView();
        }

        private void ResetView()
        {
            Bay bay = bays[index];
            Vector3 c = bay.zone.position;
            focus = new Vector3(c.x, focusHeight, c.z);
            Vector3 toFocus = focus - bay.camMarker.position;
            dist = Mathf.Clamp(toFocus.magnitude, distanceRange.x, distanceRange.y);
            Vector3 e = Quaternion.LookRotation(toFocus, Vector3.up).eulerAngles;
            yaw = baseYaw = e.y > 180f ? e.y - 360f : e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        private void Collect()
        {
            bays.Clear();
            foreach (Transform z in transform)
            {
                if (!z.name.StartsWith("Zone_")) continue;
                Transform m = z.Find("Cam_Clip");
                if (m != null) bays.Add(new Bay { zone = z, camMarker = m });
            }
            bays.Sort((a, b) => a.zone.position.x.CompareTo(b.zone.position.x));
        }

        private void OnDisable() { Exit(); }

        private void OnGUI()
        {
            if (!active || bays.Count == 0) return;
            string title = bays[index].zone.name.Replace("Zone_", "").ToUpperInvariant();
            GUIStyle big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(0, 12, Screen.width, 36), $"{title}  ({index + 1}/{bays.Count})", big);
            if (!showHelp) return;
            GUIStyle small = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.LowerCenter };
            GUI.Label(new Rect(0, Screen.height - 34, Screen.width, 28),
                "Q / E ou ← → : FX précédent / suivant   |   1-9 : choisir   |   Clic droit : orbite   |   Molette : zoom   |   R : reset   |   H : aide   |   F9 : quitter", small);
        }
    }
}
