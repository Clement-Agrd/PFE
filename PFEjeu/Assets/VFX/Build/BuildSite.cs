using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace BuildVFX
{
    /// À poser sur la racine d'un bâtiment. Play() : échafaudage → révélation de bas en haut → démontage + impact.
    [DisallowMultipleComponent]
    public class BuildSite : MonoBehaviour
    {
        [Header("Timing")]
        public float buildTime = 4.5f;
        public float tremorTime = 0.35f;
        public bool playOnStart;
        [Header("Look")]
        [ColorUsage(false, true)] public Color teamColor = new Color(2.2f, 1.1f, 0.25f);
        public Color dustColor = new Color(0.72f, 0.64f, 0.52f);
        public float edgeWidth = 0.7f, edgeGlow = 4f, noiseAmp = 0.35f;
        public bool scaffold = true, settleSquash = true, keepFoundation;
        public enum Footprint { Auto, Rectangle, Circle }
        public enum SizePreset { Custom, Auto, Cabane, Caserne, Tour, Forteresse }
        [Header("Adaptation")]
        [Tooltip("Auto : choisit Cabane / Caserne / Tour / Forteresse selon la taille et écrase buildTime, tremorTime, edgeWidth, noiseAmp, edgeGlow. Custom : garde vos valeurs.")]
        public SizePreset preset = SizePreset.Auto;
        [Tooltip("Auto : cercle pour une tour à base carrée, sinon rectangle.")]
        public Footprint footprint = Footprint.Auto;
        [Tooltip("Aligne emprise et échafaudage sur la rotation Y du bâtiment.")]
        public bool orientToBuilding = true;
        [Tooltip("Force le LOD 0 pendant l'effet pour éviter tout changement de LOD visible.")]
        public bool forceLod0 = true;
        public float impactShake = 1f;
        [Header("Assets (remplis par Tools/Build VFX/Build All)")]
        public Shader revealShader;
        public GameObject constructFx, impactFx;
        public Mesh poleMesh, plankMesh, foundationMesh;
        public Material scaffoldMat, foundationMat;
        [Header("Events")]
        public UnityEvent onStarted, onCompleted;

        public bool IsPlaying { get; private set; }
        public float Progress { get; private set; }

        static readonly int RevealY = Shader.PropertyToID("_RevealY");
        Coroutine run;
        readonly List<KeyValuePair<Renderer, Material[]>> saved = new List<KeyValuePair<Renderer, Material[]>>();
        readonly List<Material> clones = new List<Material>();
        readonly List<GameObject> spawned = new List<GameObject>();
        Vector3 baseScale;
        readonly List<LODGroup> forced = new List<LODGroup>();

        class Piece { public Transform t; public Vector3 target, vel, ang; public float delay, dur = 0.35f; public bool pole; }

        void Start() { if (playOnStart) Play(); }
        void OnDisable() { Cancel(); }

        public void Play(Color team) { teamColor = team; Play(); }
        public void Play() { Cancel(); run = StartCoroutine(Run()); }

        public void Cancel()
        {
            if (run != null) { StopCoroutine(run); run = null; }
            Restore();
            foreach (var g in spawned) if (g) Destroy(g);
            spawned.Clear();
            if (baseScale != Vector3.zero) transform.localScale = baseScale;
            IsPlaying = false;
        }

        void Restore()
        {
            foreach (var kv in saved) if (kv.Key) kv.Key.sharedMaterials = kv.Value;
            saved.Clear();
            foreach (var g in forced) if (g) g.ForceLOD(-1);
            forced.Clear();
            foreach (var m in clones) if (m) Destroy(m);
            clones.Clear();
        }

        static float Ease(float p) { float s = p * p * (3f - 2f * p); return 0.35f * p + 0.65f * s; }
        static float OutBack(float x) { x = Mathf.Clamp01(x); const float c1 = 1.7f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f); }

        Material Clone(Material o)
        {
            var m = new Material(revealShader); Texture t = null; Color c = Color.white; Vector2 sc = Vector2.one, off = Vector2.zero;
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
            m.SetColor("_EdgeColor", teamColor); m.SetFloat("_EdgeWidth", edgeWidth); m.SetFloat("_EdgeGlow", edgeGlow); m.SetFloat("_NoiseAmp", noiseAmp);
            clones.Add(m); return m;
        }
        void SetReveal(float y) { foreach (var m in clones) m.SetFloat(RevealY, y); }

        List<Renderer> Collect()
        {
            var l = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(false)) if (r is MeshRenderer || r is SkinnedMeshRenderer) l.Add(r);
            return l;
        }

        Piece AddPiece(List<Piece> list, Transform root, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, float delay, bool pole)
        {
            var g = new GameObject(pole ? "Pole" : "Plank"); g.transform.SetParent(root, false); g.transform.SetPositionAndRotation(pos, rot);
            g.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            var p = new Piece { t = g.transform, target = scale, delay = delay, pole = pole };
            g.transform.localScale = Vector3.zero; list.Add(p); return p;
        }

        void ApplyPreset(Vector2 size, float h)
        {
            var p = preset; float m = Mathf.Max(size.x, size.y);
            if (p == SizePreset.Auto) p = m < 6.5f && h < 7f ? SizePreset.Cabane : (h > 10f && h > 1.5f * m) ? SizePreset.Tour : m > 22f ? SizePreset.Forteresse : SizePreset.Caserne;
            switch (p)
            {
                case SizePreset.Cabane: buildTime = 2.6f; tremorTime = 0.2f; edgeWidth = 0.4f; noiseAmp = 0.2f; edgeGlow = 3.5f; impactShake = 0.55f; break;
                case SizePreset.Tour: buildTime = 5.5f; tremorTime = 0.35f; edgeWidth = 0.8f; noiseAmp = 0.3f; edgeGlow = 4f; impactShake = 0.8f; break;
                case SizePreset.Forteresse: buildTime = 9f; tremorTime = 0.6f; edgeWidth = 1.6f; noiseAmp = 0.6f; edgeGlow = 5f; impactShake = 1.6f; break;
            }
        }

        List<Piece> BuildScaffold(Vector3 cen, Vector2 size, float yaw, float baseY, float h, bool circle)
        {
            var list = new List<Piece>(); if (!poleMesh || !scaffoldMat) return list;
            var root = new GameObject("BuildScaffold").transform; spawned.Add(root.gameObject);
            var q = Quaternion.Euler(0, yaw, 0);
            float m = Mathf.Max(0.8f, Mathf.Min(size.x, size.y) * 0.07f), H = h * 1.04f;
            float hx = size.x * 0.5f + m, hz = size.y * 0.5f + m;
            var A = new List<Vector2>(); var B = new List<Vector2>();
            if (circle)
            {
                int n = Mathf.Max(8, Mathf.RoundToInt(Mathf.PI * (hx + hz) / 2.2f));
                for (int i = 0; i < n; i++)
                {
                    float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                    A.Add(new Vector2(Mathf.Cos(a0) * hx, Mathf.Sin(a0) * hz)); B.Add(new Vector2(Mathf.Cos(a1) * hx, Mathf.Sin(a1) * hz));
                }
            }
            else
            {
                var C = new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
                for (int s = 0; s < 4; s++)
                {
                    Vector2 a = C[s], c = C[(s + 1) % 4]; int n = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(a, c) / 2.2f));
                    for (int i = 0; i < n; i++) { A.Add(Vector2.Lerp(a, c, i / (float)n)); B.Add(Vector2.Lerp(a, c, (i + 1) / (float)n)); }
                }
            }
            int total = A.Count, levels = Mathf.Clamp(Mathf.RoundToInt(H / 3.2f), 3, 9);
            float stag = 0.9f * Mathf.Min(1f, buildTime / 3f);
            for (int i = 0; i < total; i++)
            {
                Vector3 p0 = cen + q * new Vector3(A[i].x, 0, A[i].y), p1 = cen + q * new Vector3(B[i].x, 0, B[i].y), mid = (p0 + p1) * 0.5f;
                Vector3 dir = (p1 - p0); float len = dir.magnitude; dir.y = 0; dir.Normalize();
                var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 90, 0);
                AddPiece(list, root, poleMesh, scaffoldMat, new Vector3(p0.x, baseY, p0.z), Quaternion.identity, new Vector3(1, H, 1), i / (float)total * stag, true);
                if (plankMesh) for (int k = 0; k < levels; k++)
                {
                    float l = Mathf.Lerp(0.2f, 0.92f, levels == 1 ? 0f : k / (levels - 1f));
                    AddPiece(list, root, plankMesh, scaffoldMat, new Vector3(mid.x, baseY + H * l, mid.z), rot, new Vector3(len * (circle ? 1.06f : 1f), 1f, 0.9f), l * buildTime * 0.8f, false);
                }
            }
            foreach (var p in list) { p.dur = p.pole ? 0.4f : 0.3f; }
            return list;
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

        void Frame(List<Renderer> rends, float yaw, out Vector3 cen, out Vector2 size)
        {
            var inv = Quaternion.Euler(0, -yaw, 0); var q = Quaternion.Euler(0, yaw, 0); var cs = new Vector3[8];
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = -mn; float y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var r in rends) { Corners(r, cs); foreach (var w in cs) { Vector3 l = inv * w; mn = Vector2.Min(mn, new Vector2(l.x, l.z)); mx = Vector2.Max(mx, new Vector2(l.x, l.z)); y0 = Mathf.Min(y0, w.y); y1 = Mathf.Max(y1, w.y); } }
            Vector2 mid = (mn + mx) * 0.5f; cen = q * new Vector3(mid.x, 0, mid.y); cen.y = (y0 + y1) * 0.5f; size = mx - mn;
        }

        IEnumerator Run()
        {
            IsPlaying = true; Progress = 0f; baseScale = transform.localScale; onStarted?.Invoke();
            var rends = Collect(); if (rends.Count == 0 || !revealShader) { Debug.LogWarning("[BuildVFX] Aucun renderer ou shader BuildReveal manquant."); IsPlaying = false; yield break; }
            if (forceLod0) foreach (var lg in GetComponentsInChildren<LODGroup>()) { lg.ForceLOD(0); forced.Add(lg); }
            Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
            float baseY = b.min.y, topY = b.max.y, h = topY - baseY;
            float yaw = orientToBuilding ? transform.eulerAngles.y : 0f;
            Vector3 c; Vector2 size; Frame(rends, yaw, out c, out size);
            if (preset != SizePreset.Custom) ApplyPreset(size, h);
            bool circle = footprint == Footprint.Circle || (footprint == Footprint.Auto && Mathf.Abs(size.x - size.y) < 0.12f * Mathf.Max(size.x, size.y) && h > 1.3f * Mathf.Max(size.x, size.y));
            float radius = Mathf.Max(size.x, size.y) * 0.5f;
            foreach (var r in rends) { var o = r.sharedMaterials; saved.Add(new KeyValuePair<Renderer, Material[]>(r, o)); var n = new Material[o.Length]; for (int i = 0; i < o.Length; i++) n[i] = Clone(o[i]); r.sharedMaterials = n; }
            float lo = baseY - edgeWidth - noiseAmp - 0.2f, hi = topY + noiseAmp + 0.2f; SetReveal(lo);

            GameObject slab = null;
            if (foundationMesh && foundationMat)
            {
                slab = new GameObject("BuildFoundation"); spawned.Add(slab); slab.transform.SetPositionAndRotation(new Vector3(c.x, baseY - 0.22f, c.z), Quaternion.Euler(0, yaw, 0));
                slab.AddComponent<MeshFilter>().sharedMesh = foundationMesh; slab.AddComponent<MeshRenderer>().sharedMaterial = foundationMat;
                slab.transform.localScale = new Vector3(size.x * 0.53f, 1f, size.y * 0.53f);
            }
            BuildConstructFX cfx = null;
            if (constructFx) { var g = Instantiate(constructFx); spawned.Add(g); cfx = g.GetComponent<BuildConstructFX>(); if (cfx) cfx.Setup(c, baseY, size, teamColor, dustColor, yaw); }
            var pieces = scaffold ? BuildScaffold(c, size, yaw, baseY, h, circle) : new List<Piece>();

            float t = 0f;
            while (t < buildTime)
            {
                t += Time.deltaTime; float p = Mathf.Clamp01(t / buildTime); Progress = p;
                float y = Mathf.Lerp(lo, hi, Ease(p)); SetReveal(y);
                if (cfx) cfx.SetRevealY(Mathf.Clamp(y - edgeWidth * 0.5f, baseY, topY));
                foreach (var pc in pieces)
                {
                    float k = OutBack((t - pc.delay) / pc.dur);
                    pc.t.localScale = pc.pole ? new Vector3(pc.target.x, pc.target.y * Mathf.Clamp01(k), pc.target.z) : pc.target * Mathf.Clamp01(k);
                }
                yield return null;
            }
            Progress = 1f; foreach (var pc in pieces) pc.t.localScale = pc.target;
            Restore(); if (cfx) cfx.StopEmit();

            // tremblement avant le démontage
            Vector3 center2 = new Vector3(c.x, baseY, c.z);
            for (float tt = 0; tt < tremorTime; tt += Time.deltaTime)
            {
                float a = 0.035f * (tt / tremorTime + 0.3f);
                foreach (var pc in pieces) pc.t.localPosition += new Vector3(UnityEngine.Random.Range(-a, a), 0, UnityEngine.Random.Range(-a, a)) * 0.5f;
                yield return null;
            }

            // impact
            if (impactFx) { var g = Instantiate(impactFx); var bi = g.GetComponent<BuildImpact>(); if (bi) bi.Play(center2, radius, teamColor, dustColor, impactShake); }
            if (slab && !keepFoundation) { spawned.Remove(slab); Destroy(slab, 0.05f); }
            foreach (var pc in pieces)
            {
                Vector3 d = pc.t.position - center2; d.y = 0; d = d.sqrMagnitude < 0.01f ? UnityEngine.Random.insideUnitSphere : d.normalized;
                pc.vel = d * UnityEngine.Random.Range(3f, 7f) + Vector3.up * UnityEngine.Random.Range(2f, 5f);
                pc.ang = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(180f, 540f);
            }
            const float T = 1.9f;
            for (float tt = 0; tt < T; tt += Time.deltaTime)
            {
                float dt = Time.deltaTime, fade = Mathf.Clamp01((T - tt) / 0.5f);
                foreach (var pc in pieces)
                {
                    pc.vel.y -= 18f * dt; pc.t.position += pc.vel * dt; pc.t.Rotate(pc.ang * dt, Space.World);
                    if (pc.t.position.y < baseY) { var q = pc.t.position; q.y = baseY; pc.t.position = q; pc.vel.y = -pc.vel.y * 0.3f; pc.vel.x *= 0.6f; pc.vel.z *= 0.6f; pc.ang *= 0.6f; }
                    pc.t.localScale = pc.target * fade;
                }
                if (settleSquash)
                {
                    float s = Mathf.Exp(-7f * tt) * Mathf.Cos(20f * tt) * 0.022f;
                    transform.localScale = new Vector3(baseScale.x * (1f + s * 0.5f), baseScale.y * (1f - s), baseScale.z * (1f + s * 0.5f));
                }
                yield return null;
            }
            transform.localScale = baseScale;
            foreach (var g in spawned) if (g) Destroy(g);
            spawned.Clear(); IsPlaying = false; run = null; onCompleted?.Invoke();
        }
    }
}
