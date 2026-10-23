using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace UpgradeVFX
{
    /// À poser sur la racine du bâtiment. stateA = ancien état, stateB = nouvel état (inactif au départ, même position).
    /// Play() : charge → front d'énergie qui remplace A par B de bas en haut → impact. Sans stateB : simple effet de montée en puissance.
    [DisallowMultipleComponent]
    public class UpgradeSite : MonoBehaviour
    {
        [Header("États")]
        public GameObject stateA;
        public GameObject stateB;
        public bool deactivateOld = true;
        [Header("Timing")]
        public float chargeTime = 0.9f, ascendTime = 1.9f, settleTime = 1.4f;
        public bool playOnStart;
        [Header("Look")]
        [ColorUsage(false, true)] public Color teamColor = new Color(2.2f, 1.1f, 0.25f);
        public Color dustColor = new Color(0.72f, 0.64f, 0.52f);
        public float edgeWidth = 0.6f, edgeGlow = 4f, noiseAmp = 0.3f;
        [Range(1, 3), Tooltip("Niveau d'amélioration visé : 1 sobre, 3 spectaculaire (glow, pilier, braises, secousse).")] public int tier = 1;
        public enum Footprint { Auto, Rectangle, Circle }
        public enum SizePreset { Custom, Auto, Cabane, Caserne, Tour, Forteresse }
        [Header("Adaptation")]
        [Tooltip("Auto : choisit selon la taille et écrase chargeTime, ascendTime, edgeWidth, noiseAmp, edgeGlow, impactShake. Custom : garde vos valeurs.")]
        public SizePreset preset = SizePreset.Auto;
        public Footprint footprint = Footprint.Auto;
        public bool orientToBuilding = true, forceLod0 = true;
        public float impactShake = 1f;
        [Header("Assets (remplis par Tools/Upgrade VFX/Build All)")]
        public Shader swapShader;
        public GameObject auraFx, frontFx, burstFx;
        [Header("Events")]
        public UnityEvent onStarted, onCompleted;

        public bool IsPlaying { get; private set; }
        public float Progress { get; private set; }
        static readonly int RevealY = Shader.PropertyToID("_RevealY"), ChargeId = Shader.PropertyToID("_Charge");
        Coroutine run; bool finished;
        readonly List<KeyValuePair<Renderer, Material[]>> saved = new List<KeyValuePair<Renderer, Material[]>>();
        readonly List<Material> clonesA = new List<Material>(), clonesB = new List<Material>();
        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<LODGroup> forced = new List<LODGroup>();
        Vector3 scaleA, scaleB;

        void Start() { if (playOnStart) Play(); }
        void OnDisable() { Cancel(); }
        public void Play(Color team) { teamColor = team; Play(); }
        public void Play() { Cancel(); run = StartCoroutine(Run()); }
        public void SetTier(int t) { tier = Mathf.Clamp(t, 1, 3); }

        public void Cancel()
        {
            if (run != null) { StopCoroutine(run); run = null; }
            Restore();
            foreach (var g in spawned) if (g) Destroy(g);
            spawned.Clear();
            if (IsPlaying && !finished) ResetToStateA();
            IsPlaying = false;
        }
        /// Remet A actif et B caché (pour rejouer la démo).
        public void ResetToStateA()
        {
            var a = stateA ? stateA : gameObject;
            if (a) { a.SetActive(true); if (scaleA != Vector3.zero) a.transform.localScale = scaleA; }
            if (stateB) { stateB.SetActive(false); if (scaleB != Vector3.zero) stateB.transform.localScale = scaleB; }
        }
        void Restore()
        {
            foreach (var kv in saved) if (kv.Key) kv.Key.sharedMaterials = kv.Value;
            saved.Clear();
            foreach (var g in forced) if (g) g.ForceLOD(-1);
            forced.Clear();
            foreach (var m in clonesA) if (m) Destroy(m);
            foreach (var m in clonesB) if (m) Destroy(m);
            clonesA.Clear(); clonesB.Clear();
        }

        static float Ease(float p) { float s = p * p * (3f - 2f * p); return 0.35f * p + 0.65f * s; }

        Material Clone(Material o, float keep, List<Material> bag)
        {
            var m = new Material(swapShader); Texture t = null; Color c = Color.white; Vector2 sc = Vector2.one, off = Vector2.zero;
            if (o != null)
            {
                string tp = o.HasProperty("_BaseMap") ? "_BaseMap" : (o.HasProperty("_MainTex") ? "_MainTex" : null);
                if (tp != null) { t = o.GetTexture(tp); sc = o.GetTextureScale(tp); off = o.GetTextureOffset(tp); }
                if (o.HasProperty("_BaseColor")) c = o.GetColor("_BaseColor"); else if (o.HasProperty("_Color")) c = o.GetColor("_Color");
            }
            m.SetTexture("_BaseMap", t); m.SetTextureScale("_BaseMap", sc); m.SetTextureOffset("_BaseMap", off); m.SetColor("_BaseColor", c);
            if (o != null)
            {
                if (o.HasProperty("_BumpMap") && o.GetTexture("_BumpMap") != null) { m.SetTexture("_BumpMap", o.GetTexture("_BumpMap")); m.SetFloat("_BumpScale", o.HasProperty("_BumpScale") ? o.GetFloat("_BumpScale") : 1f); }
                if (o.IsKeywordEnabled("_EMISSION") && o.HasProperty("_EmissionColor"))
                {
                    m.SetColor("_EmissionColor", o.GetColor("_EmissionColor"));
                    if (o.HasProperty("_EmissionMap") && o.GetTexture("_EmissionMap") != null) m.SetTexture("_EmissionMap", o.GetTexture("_EmissionMap"));
                }
                bool clip = o.IsKeywordEnabled("_ALPHATEST_ON") || (o.HasProperty("_AlphaClip") && o.GetFloat("_AlphaClip") > 0.5f);
                m.SetFloat("_AlphaClip", clip ? 1f : 0f);
                if (clip && o.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", o.GetFloat("_Cutoff"));
            }
            m.SetColor("_EdgeColor", teamColor); m.SetFloat("_EdgeWidth", edgeWidth); m.SetFloat("_EdgeGlow", edgeGlow * (1f + 0.15f * (tier - 1)));
            m.SetFloat("_NoiseAmp", noiseAmp); m.SetFloat("_Keep", keep); bag.Add(m); return m;
        }
        void Swap(GameObject root, float keep, List<Material> bag)
        {
            foreach (var r in Collect(root))
            {
                var o = r.sharedMaterials; saved.Add(new KeyValuePair<Renderer, Material[]>(r, o));
                var n = new Material[o.Length]; for (int i = 0; i < o.Length; i++) n[i] = Clone(o[i], keep, bag); r.sharedMaterials = n;
            }
        }
        static void SetF(List<Material> l, int id, float v) { foreach (var m in l) m.SetFloat(id, v); }

        static List<Renderer> Collect(GameObject go)
        {
            var l = new List<Renderer>();
            if (go) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r is MeshRenderer || r is SkinnedMeshRenderer) l.Add(r);
            return l;
        }
        static void Corners(Renderer r, Vector3[] c)
        {
            Bounds lb; Transform t = null;
            if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) { lb = mf.sharedMesh.bounds; t = r.transform; } else lb = r.bounds; }
            else lb = r.bounds;
            Vector3 e = lb.extents, ce = lb.center; int k = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            { Vector3 p = ce + new Vector3(e.x * x, e.y * y, e.z * z); c[k++] = t ? t.TransformPoint(p) : p; }
        }
        static void Frame(List<Renderer> rends, float yaw, out Vector3 cen, out Vector2 size, out float y0, out float y1)
        {
            var inv = Quaternion.Euler(0, -yaw, 0); var q = Quaternion.Euler(0, yaw, 0); var cs = new Vector3[8];
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = -mn; y0 = float.MaxValue; y1 = float.MinValue;
            foreach (var r in rends) { Corners(r, cs); foreach (var w in cs) { Vector3 l = inv * w; mn = Vector2.Min(mn, new Vector2(l.x, l.z)); mx = Vector2.Max(mx, new Vector2(l.x, l.z)); y0 = Mathf.Min(y0, w.y); y1 = Mathf.Max(y1, w.y); } }
            Vector2 mid = (mn + mx) * 0.5f; cen = q * new Vector3(mid.x, 0, mid.y); cen.y = (y0 + y1) * 0.5f; size = mx - mn;
        }
        void ApplyPreset(Vector2 size, float h)
        {
            var p = preset; float m = Mathf.Max(size.x, size.y);
            if (p == SizePreset.Auto) p = m < 6.5f && h < 7f ? SizePreset.Cabane : (h > 10f && h > 1.5f * m) ? SizePreset.Tour : m > 22f ? SizePreset.Forteresse : SizePreset.Caserne;
            switch (p)
            {
                case SizePreset.Cabane: chargeTime = 0.6f; ascendTime = 1.2f; edgeWidth = 0.35f; noiseAmp = 0.18f; edgeGlow = 3.5f; impactShake = 0.5f; break;
                case SizePreset.Caserne: chargeTime = 0.9f; ascendTime = 1.9f; edgeWidth = 0.6f; noiseAmp = 0.3f; edgeGlow = 4f; impactShake = 1f; break;
                case SizePreset.Tour: chargeTime = 0.9f; ascendTime = 2.6f; edgeWidth = 0.7f; noiseAmp = 0.3f; edgeGlow = 4f; impactShake = 0.8f; break;
                case SizePreset.Forteresse: chargeTime = 1.2f; ascendTime = 3.2f; edgeWidth = 1.4f; noiseAmp = 0.55f; edgeGlow = 5f; impactShake = 1.6f; break;
            }
        }

        IEnumerator Run()
        {
            IsPlaying = true; finished = false; Progress = 0f; onStarted?.Invoke();
            var A = stateA ? stateA : gameObject; var B = stateB; bool swap = B != null;
            if (!swapShader) { Debug.LogWarning("[UpgradeVFX] Shader UpgradeSwap manquant (lance Tools/Upgrade VFX/Build All)."); IsPlaying = false; yield break; }
            if (swap) B.SetActive(true);
            var rA = Collect(A); var rB = Collect(B); var all = new List<Renderer>(rA); all.AddRange(rB);
            if (rA.Count == 0) { Debug.LogWarning("[UpgradeVFX] Aucun renderer sur l'état A."); IsPlaying = false; yield break; }
            scaleA = A.transform.localScale; if (swap) scaleB = B.transform.localScale;
            if (forceLod0) foreach (var go in new[] { A, B }) if (go) foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) { lg.ForceLOD(0); forced.Add(lg); }
            float yaw = orientToBuilding ? A.transform.eulerAngles.y : 0f;
            Vector3 c; Vector2 size; float baseY, topY; Frame(all, yaw, out c, out size, out baseY, out topY); float h = topY - baseY;
            if (preset != SizePreset.Custom) ApplyPreset(size, h);
            float k = 1f + 0.3f * (tier - 1);
            bool circle = footprint == Footprint.Circle || (footprint == Footprint.Auto && Mathf.Abs(size.x - size.y) < 0.12f * Mathf.Max(size.x, size.y) && h > 1.3f * Mathf.Max(size.x, size.y));
            Swap(A, swap ? -1f : 1f, clonesA); if (swap) Swap(B, 1f, clonesB);
            float lo = baseY - edgeWidth - noiseAmp - 0.2f, hi = topY + edgeWidth + noiseAmp + 0.3f;
            float clipOff = swap ? lo : 1e5f;
            SetF(clonesA, RevealY, clipOff); SetF(clonesB, RevealY, lo);
            Vector3 foot = new Vector3(c.x, baseY, c.z);

            UpgradeAura aura = null; UpgradeFront front = null;
            if (auraFx) { var g = Instantiate(auraFx); spawned.Add(g); aura = g.GetComponent<UpgradeAura>(); if (aura) { aura.Setup(c, baseY, size, yaw, h, teamColor, dustColor, k); aura.BeginCharge(); } }
            if (frontFx) { var g = Instantiate(frontFx); spawned.Add(g); front = g.GetComponent<UpgradeFront>(); if (front) { front.Setup(c, size, yaw, circle, teamColor, edgeWidth, k); front.SetY(lo); } }

            // 1. charge
            for (float t = 0; t < chargeTime; t += Time.deltaTime)
            {
                float p = t / chargeTime, e = Ease(p), s = Mathf.Sin(p * Mathf.PI);
                SetF(clonesA, ChargeId, e); if (aura) aura.SetCharge(e);
                A.transform.localScale = new Vector3(scaleA.x * (1f + 0.01f * s), scaleA.y * (1f - 0.02f * s), scaleA.z * (1f + 0.01f * s));
                Progress = p * 0.25f; yield return null;
            }
            A.transform.localScale = scaleA; SetF(clonesA, ChargeId, 1f); SetF(clonesB, ChargeId, 1f);

            // 2. montée du front
            if (aura) aura.BeginAscend(); if (front) front.Show(true);
            for (float t = 0; t < ascendTime; t += Time.deltaTime)
            {
                float p = Mathf.Clamp01(t / ascendTime), y = Mathf.Lerp(lo, hi, Ease(p));
                if (swap) { SetF(clonesA, RevealY, y); SetF(clonesB, RevealY, y); }
                if (front) front.SetY(y);
                if (aura) aura.SetCharge(1f);
                Progress = 0.25f + p * 0.6f; yield return null;
            }
            if (front) front.Finish(); if (aura) aura.Finish();

            // 3. impact
            if (swap && deactivateOld) A.SetActive(false);
            if (burstFx) { var g = Instantiate(burstFx); var bu = g.GetComponent<UpgradeBurst>(); if (bu) bu.Play(foot, size, h, teamColor, dustColor, k, impactShake); }
            for (float t = 0; t < settleTime; t += Time.deltaTime)
            {
                float p = t / settleTime, s = 0.04f * Mathf.Exp(-6f * t) * Mathf.Cos(18f * t);
                var target = swap ? B : A; var bs = swap ? scaleB : scaleA;
                target.transform.localScale = new Vector3(bs.x * (1f - s * 0.5f), bs.y * (1f + s), bs.z * (1f - s * 0.5f));
                float ch = Mathf.Clamp01(1f - p * 1.3f); SetF(clonesA, ChargeId, ch); SetF(clonesB, ChargeId, ch);
                Progress = 0.85f + p * 0.15f; yield return null;
            }
            if (swap) B.transform.localScale = scaleB; else A.transform.localScale = scaleA;
            Restore(); finished = true;
            foreach (var g in spawned) if (g && !g.GetComponent<UpgradeBurst>()) { }
            spawned.Clear(); IsPlaying = false; run = null; Progress = 1f; onCompleted?.Invoke();
        }
    }
}
