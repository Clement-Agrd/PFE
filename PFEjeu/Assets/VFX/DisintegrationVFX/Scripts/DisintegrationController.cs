using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.DisintegrationVFX
{
    /// <summary>
    /// Désintègre un ennemi (SkinnedMeshRenderer / MeshRenderer) en papier qui brûle.
    /// Swap des matériaux par une copie VFX/Disintegrate qui reprend albedo/couleur de l'original ;
    /// Reset() restaure tout (ressuscitation / pooling).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DisintegrationController : MonoBehaviour
    {
        private enum State { Idle, Playing, Done }

        [Header("Style")]
        [Tooltip("Vide = style par défaut de DisintegrationConfig.")]
        [SerializeField] private DisintegrationStyle style;

        [Header("Timeline (secondes à la durée de référence 2.2 s)")]
        [Tooltip("Corps figé + flash avant que le feu ne démarre.")]
        [SerializeField, Range(0f, 0.5f)] private float anticipation = 0.15f;
        [SerializeField, Range(0, 6)] private int hitStopFrames = 2;
        [Tooltip("Courbe de progression du front (0..1 -> 0..1).")]
        [SerializeField] private AnimationCurve progressCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.6f), new Keyframe(0.5f, 0.5f, 1.05f, 1.05f), new Keyframe(1f, 1f, 0.8f, 0f));

        [Header("Désactivation à la fin")]
        [SerializeField] private bool disableColliders = true;
        [SerializeField] private bool freezeAnimators = true;

        public event Action OnDisintegrated;

        public bool IsPlaying => _state == State.Playing;
        public bool IsDone => _state == State.Done;
        public DisintegrationStyle Style { get => style; set => style = value; }

        // ids shader
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int FlashId = Shader.PropertyToID("_FlashAmount");
        private static readonly int OriginId = Shader.PropertyToID("_BurnOrigin");
        private static readonly int DirId = Shader.PropertyToID("_BurnDir");
        private static readonly int RangeId = Shader.PropertyToID("_BurnRange");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetalId = Shader.PropertyToID("_Metallic");
        private static readonly int EmisId = Shader.PropertyToID("_EmissionColor");

        // buffers statiques partagés (jamais utilisés de façon concurrente dans une même frame)
        private static readonly List<Vector3> TmpV = new List<Vector3>(8192);
        private static readonly List<Vector3> TmpN = new List<Vector3>(8192);
        private static Mesh _bake;
        private static Texture2D _noiseTex;
        private float _patchAmount, _patchScale;

        private State _state;
        private Renderer[] _renderers;
        private Material[][] _originals;
        private Material[][] _burn;
        private Collider[] _colliders;
        private bool[] _colliderStates;
        private Animator[] _animators;
        private float[] _animSpeeds;
        private bool[] _rendererStates;

        private Vector3[] _pts = new Vector3[0];
        private Vector3[] _nrm = new Vector3[0];
        private float[] _t = new float[0];
        private int _count;

        private DisintegrationFX _fx;
        private DisintegrationStyle _active;
        private DisintegrationQuality _quality;
        private Vector3 _origin, _dir, _center;
        private float _range, _height, _minY, _maxY, _duration, _time, _p;
        private int _stopFrames;
        private bool _radial;
        private float _accScrap, _accAsh, _accEmber, _accSmoke;

        // ---------------------------------------------------------------- API

        /// <summary>Ajoute le composant si besoin (n'importe quel ennemi).</summary>
        public static DisintegrationController GetOrAdd(GameObject go)
        {
            if (DisintegrationConfig.Instance == null)
                return null;
            if (!go.TryGetComponent(out DisintegrationController c))
                c = go.AddComponent<DisintegrationController>();
            return c;
        }

        public bool Disintegrate(float duration = -1f, bool disableHitboxImmediately = false)
        {
            return Begin(false, default, duration, disableHitboxImmediately);
        }

        public bool Disintegrate(Vector3 hitPoint, float duration = -1f)
        {
            return Begin(true, hitPoint, duration);
        }

        private void OnEnable()
        {
            // réutilisation depuis un pool : on repart d'un ennemi intact
            if (_state != State.Idle)
                Reset();
        }

        private void OnDisable()
        {
            if (_state == State.Playing)
                Reset();
        }

        private bool Begin(bool fromHit, Vector3 hit, float duration, bool disableHitboxImmediately = false)
        {
            if (_state == State.Playing)
                return true;
            if (_state == State.Done)
                Reset();

            DisintegrationConfig cfg = DisintegrationConfig.Instance;
            if (cfg == null || cfg.template == null)
            {
                Debug.LogWarning("[Disintegration] Config introuvable (Resources/DisintegrationConfig).", this);
                return false;
            }

            _active = style != null ? style : cfg.defaultStyle;
            if (_active == null)
                return false;

            CollectRenderers();
            if (_renderers.Length == 0)
            {
                Debug.LogWarning("[Disintegration] Aucun renderer : repli sans effet.", this);
                return false;
            }

            _noiseTex = cfg.template.GetTexture("_BurnNoise") as Texture2D;
            _patchAmount = _noiseTex != null && _noiseTex.isReadable ? _active.patchiness : 0f;
            _patchScale = _active.patchScale;
            if (_noiseTex != null && !_noiseTex.isReadable)
                Debug.LogWarning("[Disintegration] T_BurnNoise non lisible : desintegration par morceaux desactivee.", this);
            _quality = cfg.quality;
            _duration = duration > 0f ? duration : _active.duration;
            if (!Bake(fromHit || _active.direction == BurnDirection.FromHitPoint, hit))
                return false;

            BuildMaterials(cfg.template);
            ApplyStyleToMaterials();
            SwapMaterials(true);

            if (disableHitboxImmediately)
            {
                foreach (var collider in _colliders)
                    if (collider != null) collider.enabled = false;
            }

            if (freezeAnimators)
            {
                for (int i = 0; i < _animators.Length; i++)
                {
                    _animSpeeds[i] = _animators[i].speed;
                    _animators[i].speed = 0f;
                }
            }

            _fx = DisintegrationManager.Rent();
            if (_fx != null)
            {
                bool ground = Physics.Raycast(new Vector3(_center.x, _minY + 0.5f, _center.z), Vector3.down, out RaycastHit h, 3f, ~0, QueryTriggerInteraction.Ignore);
                _fx.Begin(_active, _quality, _center, ground ? h.point : new Vector3(_center.x, _minY, _center.z), ground,
                    Mathf.Max(_bounds.size.x, _bounds.size.z));
            }

            _time = 0f;
            _p = 0f;
            _stopFrames = hitStopFrames;
            _accScrap = _accAsh = _accEmber = _accSmoke = 0f;
            SetProgress(0f, 0f);
            _state = State.Playing;
            DisintegrationManager.Register(this);
            return true;
        }

        /// <summary>Remet l'ennemi dans son état d'origine (matériaux, colliders, animators).</summary>
        public void Reset()
        {
            if (_state == State.Idle)
                return;

            DisintegrationManager.Unregister(this);
            if (_fx != null)
            {
                _fx.Kill();
                _fx = null;
            }

            SwapMaterials(false);
            RestoreState();
            _state = State.Idle;
        }

        /// <summary>Terminé immédiatement (limite globale) — le FX s'éteint seul.</summary>
        public void ForceFinish()
        {
            if (_state == State.Playing)
                Complete();
        }

        // ---------------------------------------------------------------- init

        private Bounds _bounds;

        private void CollectRenderers()
        {
            if (_renderers != null && _renderers.Length > 0)
                return;

            var list = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer || r is MeshRenderer)
                    list.Add(r);
            }

            _renderers = list.ToArray();
            _originals = new Material[_renderers.Length][];
            _burn = new Material[_renderers.Length][];
            _rendererStates = new bool[_renderers.Length];
            _colliders = GetComponentsInChildren<Collider>(true);
            _colliderStates = new bool[_colliders.Length];
            _animators = GetComponentsInChildren<Animator>(true);
            _animSpeeds = new float[_animators.Length];
        }

        private bool Bake(bool radial, Vector3 hit)
        {
            if (_bake == null)
                _bake = new Mesh { name = "DisintegrationBake" };
            _bake.MarkDynamic();

            _count = 0;
            bool first = true;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (!r.enabled || !r.gameObject.activeInHierarchy)
                    continue;

                TmpV.Clear();
                TmpN.Clear();
                Matrix4x4 m;
                if (r is SkinnedMeshRenderer smr)
                {
                    if (smr.sharedMesh == null) continue;
                    smr.BakeMesh(_bake, true);
                    _bake.GetVertices(TmpV);
                    _bake.GetNormals(TmpN);
                    m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                }
                else
                {
                    Mesh mesh = ((MeshRenderer)r).GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue;
                    mesh.GetVertices(TmpV);
                    mesh.GetNormals(TmpN);
                    m = r.transform.localToWorldMatrix;
                }

                int stride = Mathf.Max(1, TmpV.Count / 2500);
                EnsureCapacity(_count + TmpV.Count / stride + 1);
                for (int v = 0; v < TmpV.Count; v += stride)
                {
                    Vector3 w = m.MultiplyPoint3x4(TmpV[v]);
                    _pts[_count] = w;
                    _nrm[_count] = v < TmpN.Count ? m.MultiplyVector(TmpN[v]).normalized : Vector3.up;
                    if (first) { _bounds = new Bounds(w, Vector3.zero); first = false; }
                    else _bounds.Encapsulate(w);
                    _count++;
                }
            }

            if (_count == 0)
            {
                // maillage illisible : repli sur les bounds des renderers
                Debug.LogWarning("[Disintegration] Mesh illisible, front approximé par les bounds.", this);
                _bounds = _renderers[0].bounds;
                for (int i = 1; i < _renderers.Length; i++)
                    _bounds.Encapsulate(_renderers[i].bounds);
                EnsureCapacity(64);
                for (int i = 0; i < 64; i++)
                {
                    _pts[i] = _bounds.center + new Vector3(
                        (UnityEngine.Random.value - 0.5f) * _bounds.size.x,
                        (UnityEngine.Random.value - 0.5f) * _bounds.size.y,
                        (UnityEngine.Random.value - 0.5f) * _bounds.size.z);
                    _nrm[i] = (_pts[i] - _bounds.center).normalized;
                }
                _count = 64;
            }

            _center = _bounds.center;
            _minY = _bounds.min.y;
            _maxY = _bounds.max.y;
            _height = Mathf.Max(0.1f, _maxY - _minY);
            _radial = radial || _active.direction == BurnDirection.FromHitPoint;

            if (_radial)
            {
                _origin = hit == default ? _center : hit;
                _dir = Vector3.up;
                float far = 0.1f;
                for (int i = 0; i < _count; i++)
                    far = Mathf.Max(far, (_pts[i] - _origin).magnitude);
                _range = far;
            }
            else if (_active.direction == BurnDirection.TopDown)
            {
                _origin = new Vector3(_center.x, _maxY, _center.z);
                _dir = Vector3.down;
                _range = _height;
            }
            else
            {
                _origin = new Vector3(_center.x, _minY, _center.z);
                _dir = Vector3.up;
                _range = _height;
            }

            for (int i = 0; i < _count; i++)
                _t[i] = BurnBase(_pts[i]);
            return true;
        }

        private void EnsureCapacity(int n)
        {
            if (_pts.Length >= n) return;
            Array.Resize(ref _pts, n);
            Array.Resize(ref _nrm, n);
            Array.Resize(ref _t, n);
        }

        private float BurnBase(Vector3 wp)
        {
            Vector3 d = wp - _origin;
            float spatial = (_radial ? d.magnitude : Vector3.Dot(d, _dir)) / Mathf.Max(_range, 0.001f);
            return _patchAmount > 0f ? Mathf.Lerp(spatial, PatchField(wp), _patchAmount) : spatial;
        }

        // Meme formule que Disintegrate.shader (PatchField) : bilineaire, texture Repeat.
        private float PatchField(Vector3 wp)
        {
            Vector3 p = wp * _patchScale;
            float a = _noiseTex.GetPixelBilinear(p.x + p.z * 0.5f, p.y).r;
            float b = _noiseTex.GetPixelBilinear(p.z - p.x * 0.5f, p.y * 1.3f + 0.37f).r;
            return Mathf.Clamp01(((a + b) * 0.5f - 0.5f) * 3.0f + 0.5f);
        }

        private void BuildMaterials(Material template)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material[] src = _renderers[i].sharedMaterials;
                if (_originals[i] == null || _originals[i].Length != src.Length)
                {
                    _originals[i] = src;
                    _burn[i] = new Material[src.Length];
                }
                else
                {
                    // si l'ennemi a changé de matériaux entre deux morts on garde l'ordre d'origine
                    src = _originals[i];
                }

                for (int s = 0; s < src.Length; s++)
                {
                    Material o = src[s];
                    if (_burn[i][s] == null)
                        _burn[i][s] = new Material(template) { name = "Disintegrate (inst)" };

                    Material b = _burn[i][s];
                    if (o == null)
                        continue;

                    if (o.HasProperty(BaseMapId)) b.SetTexture(BaseMapId, o.GetTexture(BaseMapId));
                    else if (o.HasProperty(MainTexId)) b.SetTexture(BaseMapId, o.GetTexture(MainTexId));

                    if (o.HasProperty(BaseColorId)) b.SetColor(BaseColorId, o.GetColor(BaseColorId));
                    else if (o.HasProperty(ColorId)) b.SetColor(BaseColorId, o.GetColor(ColorId));
                    else if (!o.HasProperty(BaseMapId) && !o.HasProperty(MainTexId))
                        Debug.LogWarning("[Disintegration] Matériau '" + o.name + "' sans propriétés URP Lit : repli sur dissolve simple (gris).", this);

                    if (o.HasProperty(SmoothId)) b.SetFloat(SmoothId, o.GetFloat(SmoothId));
                    if (o.HasProperty(MetalId)) b.SetFloat(MetalId, o.GetFloat(MetalId));
                    bool hasEmisMap = o.HasProperty("_EmissionMap") && o.GetTexture("_EmissionMap") != null;
                    b.SetColor(EmisId, !hasEmisMap && o.HasProperty(EmisId) && o.IsKeywordEnabled("_EMISSION") ? o.GetColor(EmisId) : Color.black);
                }
            }
        }

        private void ApplyStyleToMaterials()
        {
            DisintegrationStyle st = _active;
            Vector4 dir = new Vector4(_dir.x, _dir.y, _dir.z, _radial ? 1f : 0f);
            for (int i = 0; i < _burn.Length; i++)
            {
                for (int s = 0; s < _burn[i].Length; s++)
                {
                    Material b = _burn[i][s];
                    if (b == null) continue;
                    b.SetTexture("_BurnRamp", st.ramp);
                    b.SetFloat("_NoiseScale", st.noiseScale);
                    b.SetFloat("_EdgeWidth", st.edgeWidth);
                    b.SetFloat("_CharWidth", st.charWidth);
                    b.SetFloat("_CurlAmount", st.curl);
                    b.SetFloat("_NoiseAmp", st.noiseAmp);
                    b.SetFloat("_EdgeIntensity", st.edgeIntensity);
                    b.SetColor("_CharColor", st.charColor);
                    b.SetColor("_FlashColor", st.flashColor);
                    b.SetVector(OriginId, _origin);
                    b.SetVector(DirId, dir);
                    b.SetFloat(RangeId, _range);
                    b.SetFloat("_PatchAmount", _patchAmount);
                    b.SetFloat("_PatchScale", _patchScale);
                }
            }
        }

        private void SwapMaterials(bool toBurn)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                if (toBurn) _rendererStates[i] = _renderers[i].enabled;
                _renderers[i].sharedMaterials = toBurn ? _burn[i] : _originals[i];
                if (!toBurn) _renderers[i].enabled = _rendererStates[i];
            }

            if (toBurn)
            {
                for (int i = 0; i < _colliders.Length; i++)
                    _colliderStates[i] = _colliders[i] != null && _colliders[i].enabled;
            }
        }

        private void RestoreState()
        {
            if (_colliders != null)
            {
                for (int i = 0; i < _colliders.Length; i++)
                    if (_colliders[i] != null) _colliders[i].enabled = _colliderStates[i];
            }

            if (_animators != null && freezeAnimators)
            {
                for (int i = 0; i < _animators.Length; i++)
                    if (_animators[i] != null) _animators[i].speed = _animSpeeds[i] > 0f ? _animSpeeds[i] : 1f;
            }
        }

        // ---------------------------------------------------------------- update

        private void Update()
        {
            if (_state != State.Playing)
                return;

            if (_stopFrames > 0)
            {
                // hit stop : le corps se fige, flash plein
                _stopFrames--;
                SetProgress(0f, 1f);
                return;
            }

            float k = _duration / 2.2f;
            float anti = anticipation * k;
            _time += Time.deltaTime;

            float u = Mathf.Clamp01((_time - anti) / Mathf.Max(0.01f, _duration - anti));
            _p = progressCurve.Evaluate(u);
            float flash = _time < anti ? Mathf.Lerp(1f, 0.6f, _time / Mathf.Max(0.01f, anti)) : Mathf.Clamp01(1f - (_time - anti) / (0.12f * k)) * 0.6f;
            SetProgress(_p, flash);

            if (_time >= anti && _fx != null)
                Emit(u, Time.deltaTime);

            if (_time >= _duration)
                Complete();
        }

        private void SetProgress(float p, float flash)
        {
            for (int i = 0; i < _burn.Length; i++)
            {
                for (int s = 0; s < _burn[i].Length; s++)
                {
                    Material b = _burn[i][s];
                    if (b == null) continue;
                    b.SetFloat(ProgressId, p);
                    b.SetFloat(FlashId, flash);
                }
            }
        }

        private float FrontPos(float p)
        {
            float amp = _active.noiseAmp;
            float end = 1f + amp + _active.edgeWidth + _active.charWidth * 1.4f + 0.02f;
            return Mathf.Lerp(-amp - 0.02f, end, p);
        }

        private void Emit(float u, float dt)
        {
            DisintegrationFX fx = _fx;
            float scale = fx.Scale;
            float front = FrontPos(_p);
            float band = _active.edgeWidth + _active.charWidth * 0.5f;
            float burnTime = Mathf.Max(0.5f, _duration * 0.7f);
            float crumble = u > 0.6f ? 2f : 1f;

            _accScrap += _active.scrapCount / burnTime * dt * scale * crumble;
            _accAsh += _active.ashCount / burnTime * dt * scale;
            _accEmber += _active.emberCount / burnTime * dt * scale;
            _accSmoke += _active.smokeCount / burnTime * dt * (fx.UseSmoke ? 1f : 0f);

            while (_accScrap >= 1f) { _accScrap -= 1f; if (Pick(front, band, out int i)) fx.EmitScrap(_pts[i], _nrm[i], _center); }
            while (_accAsh >= 1f) { _accAsh -= 1f; if (Pick(front, band, out int i)) fx.EmitAsh(_pts[i], _nrm[i], _center); }
            while (_accEmber >= 1f) { _accEmber -= 1f; if (Pick(front, band * 0.6f, out int i)) fx.EmitEmber(_pts[i], _nrm[i], _center); }
            while (_accSmoke >= 1f) { _accSmoke -= 1f; if (Pick(front, band, out int i)) fx.EmitSmoke(_pts[i]); }

            Vector3 fp = _radial
                ? Vector3.Lerp(_origin, _center, Mathf.Clamp01(front))
                : new Vector3(_center.x, Mathf.Clamp(_origin.y + _dir.y * front * _range, _minY, _maxY), _center.z);
            float glowI = Mathf.Clamp01(Mathf.Min(_p * 8f, (1f - _p) * 6f));
            fx.SetFront(fp, glowI);
            fx.SetScorchAppear(Mathf.Clamp01(_p * 1.6f));
        }

        private bool Pick(float front, float band, out int index)
        {
            for (int tries = 0; tries < 10; tries++)
            {
                int i = UnityEngine.Random.Range(0, _count);
                float s = front - _t[i];
                if (s >= 0f && s <= band)
                {
                    index = i;
                    return true;
                }
            }

            index = 0;
            return false;
        }

        private void Complete()
        {
            if (_state != State.Playing)
                return;

            _state = State.Done;
            DisintegrationManager.Unregister(this);
            SetProgress(1f, 0f);

            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = false;

            if (disableColliders)
            {
                for (int i = 0; i < _colliders.Length; i++)
                    if (_colliders[i] != null) _colliders[i].enabled = false;
            }

            if (_fx != null)
            {
                _fx.Finish();
                _fx = null;
            }

            OnDisintegrated?.Invoke();
        }
    }
}
