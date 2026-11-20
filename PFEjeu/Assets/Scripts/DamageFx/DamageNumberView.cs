using TMPro;
using UnityEngine;

namespace DamageFx
{
    /// <summary>
    /// Vue d'UN chiffre (TextMeshPro 3D + icône + éclat de crit). Pas d'Update : le gestionnaire appelle Tick().
    /// Cycle : ouvert (accepte des coups qui s'additionnent, le chiffre pulse) puis fermé (montée en arc + fondu sur la fin).
    /// </summary>
    public sealed class DamageNumberView : MonoBehaviour, IDamageNumberView
    {
        #region Références
        [SerializeField] private TextMeshPro label;
        [SerializeField] private SpriteRenderer icon;
        [SerializeField, Tooltip("Éclat / étoile derrière un crit.")] private SpriteRenderer burst;
        [SerializeField] private DamageNumbersSettings settings;
        #endregion

        #region État
        private const float GlyphUnit = 0.1f;     // TMP 3D : une lettre de police N fait ~N x 0,1 unité locale
        private readonly float[] kindTotals = new float[DamageFxKinds.Count];
        private DamageTypeStyle style;
        private DamageTypeStyle[] stylesByKind;
        private DamageTypeStyle critStyle;
        private DamageFxKind dominant;
        private float total;
        private float maxHealth;
        private bool crit, boss, small, closed;
        private long targetKey;
        private Transform target;
        private Vector3 anchorWorld, localOffset, stackOffset;
        private bool hasTarget;
        private float age, spawnTime, lastHitTime, closeAge, openRise, sideSign, roll;
        private int flashFrames;
        private float pulse, shakeLeft;
        private float lastAlpha = -1f;
        private bool flashShown;
        private Transform cachedTransform;
        #endregion

        #region IDamageNumberView
        public bool IsOpen => !closed;
        public bool IsHeal => dominant == DamageFxKind.Heal;
        public long TargetKey => targetKey;
        public float SpawnTime => spawnTime;
        public float LastHitTime => lastHitTime;
        public Vector3 AnchorPosition => cachedTransform != null ? cachedTransform.position : Vector3.zero;

        public int Priority
        {
            get
            {
                if (crit) return 40;
                if (boss) return 35;
                if (small) return 0;
                return style != null ? style.Priority : 10;
            }
        }

        public float Distance(Vector3 cameraPosition) => (cachedTransform.position - cameraPosition).magnitude;

        /// <summary>Appelé une fois par le gestionnaire à la création (styles par famille + style de crit).</summary>
        public void Configure(DamageNumbersSettings value, DamageTypeStyle[] byKind, DamageTypeStyle crit)
        {
            settings = value; stylesByKind = byKind; critStyle = crit;
            cachedTransform = transform;
        }

        public void Begin(in DamageHit hit, Vector3 stack)
        {
            if (cachedTransform == null) cachedTransform = transform;
            System.Array.Clear(kindTotals, 0, kindTotals.Length);
            total = 0f;
            crit = hit.IsCrit;
            boss = hit.IsBoss;
            closed = false;
            age = 0f; closeAge = 0f; openRise = 0f; pulse = 0f;
            spawnTime = Time.time;
            lastHitTime = spawnTime;
            lastAlpha = -1f;
            flashFrames = crit ? 2 : 0;
            flashShown = false;
            shakeLeft = crit ? settings.CritShakeDuration : 0f;
            sideSign = Random.value < 0.5f ? -1f : 1f;
            roll = Random.Range(-settings.RollDegrees, settings.RollDegrees);
            stackOffset = stack;
            targetKey = hit.TargetKey;
            target = hit.Target;
            hasTarget = target != null;
            anchorWorld = hit.Point;
            localOffset = hasTarget ? hit.Point - target.position : Vector3.zero;
            maxHealth = hit.TargetMaxHealth;
            style = null;
            dominant = hit.Kind;

            gameObject.SetActive(true);
            AccumulateInto(hit);
            ApplyStyle(true);
            cachedTransform.localScale = Vector3.zero;
            cachedTransform.position = hit.Point + stackOffset;
        }

        public void AddHit(in DamageHit hit)
        {
            lastHitTime = Time.time;
            pulse = 1f;
            crit |= hit.IsCrit;
            AccumulateInto(hit);
            ApplyStyle(false);
        }

        public bool Tick(float dt, float now, Camera cam)
        {
            age += dt;
            if (!closed && (now - lastHitTime > CurrentWindow() || now - spawnTime > settings.MaxAccumulationTime))
                closed = true;

            Vector3 anchor = anchorWorld;
            if (hasTarget && target != null) anchor = target.position + localOffset;
            anchorWorld = anchor;

            float life = settings.Life * (style != null ? style.LifeMultiplier : 1f);
            float rise = settings.Rise * (style != null ? style.RiseMultiplier : 1f);
            Vector3 pos;
            float t = 0f;
            if (!closed)
            {
                openRise = Mathf.Min(0.3f * rise, age * 0.6f);
                pos = anchor + stackOffset + Vector3.up * openRise;
            }
            else
            {
                closeAge += dt;
                t = Mathf.Clamp01(closeAge / life);
                float ease = 1f - (1f - t) * (1f - t);
                Vector3 side = cam != null ? cam.transform.right : Vector3.right;
                pos = anchor + stackOffset + Vector3.up * (openRise + rise * ease) + side * (sideSign * settings.Arc * Mathf.Sin(t * Mathf.PI));
            }

            float dist = cam != null ? (pos - cam.transform.position).magnitude : settings.ReferenceDistance;
            float factor = Mathf.Clamp(dist / settings.ReferenceDistance, settings.MinDistanceFactor, settings.MaxDistanceFactor);
            bool big = crit || boss || total >= settings.BigHit || (style != null && style.Kind == DamageFxKind.TowerHit);
            float farShrink = 1f;
            bool visible = true;
            if (!big && dist > settings.HideDistance * 0.6f)
            {
                farShrink = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(settings.HideDistance * 0.6f, settings.HideDistance, dist));
                visible = dist <= settings.HideDistance;
            }
            else if (dist > settings.FarDistance) visible = false;

            float size = SizeMultiplier();
            float popT = Mathf.Clamp01(age / settings.PopDuration);
            float pop = style != null ? style.PopCurve.Evaluate(popT) : popT;
            float pulseScale = 1f;
            if (pulse > 0f)
            {
                pulse = Mathf.Max(0f, pulse - dt / 0.18f);
                pulseScale = 1f + 0.28f * Mathf.Sin(pulse * Mathf.PI);
            }
            cachedTransform.localScale = Vector3.one * (settings.BaseWorldScale * factor * size * farShrink * pop * pulseScale);

            if (shakeLeft > 0f)
            {
                shakeLeft -= dt;
                Vector2 s = Random.insideUnitCircle * (settings.CritShakeAmplitude * (shakeLeft / settings.CritShakeDuration));
                Vector3 right = cam != null ? cam.transform.right : Vector3.right;
                pos += right * s.x + Vector3.up * s.y;
            }
            cachedTransform.position = pos;
            cachedTransform.rotation = cam != null ? cam.transform.rotation * Quaternion.Euler(0f, 0f, roll) : Quaternion.Euler(0f, 0f, roll);

            float alpha = 1f;
            if (closed && t > settings.FadeStart)
            {
                float f = Mathf.InverseLerp(settings.FadeStart, 1f, t);
                alpha = 1f - f * f * (3f - 2f * f);
            }
            UpdateColors(alpha, visible);

            return !(closed && t >= 1f);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            target = null;
            style = null;
        }
        #endregion

        #region Interne
        private float CurrentWindow() => small ? settings.SmallAccumulationWindow : settings.AccumulationWindow;

        private void AccumulateInto(in DamageHit hit)
        {
            kindTotals[(int)hit.Kind] += hit.Amount;
            total += hit.Amount;
            if (hit.TargetMaxHealth > 0f) maxHealth = hit.TargetMaxHealth;
            // Famille dominante = celle qui a fait le plus de dégâts dans ce cumul.
            int best = 0;
            for (int i = 1; i < kindTotals.Length; i++) if (kindTotals[i] > kindTotals[best]) best = i;
            dominant = (DamageFxKind)best;
        }

        private float SizeMultiplier()
        {
            float k = Mathf.Clamp01(total / settings.MaxDamageReference);
            float m = Mathf.Lerp(settings.MinSizeMultiplier, settings.MaxSizeMultiplier, k);
            if (style != null) m *= style.Scale;
            if (crit) m *= settings.CritScale;
            if (small) m *= settings.SmallScale;
            return m;
        }

        /// <summary>Texte, matériau, couleurs et icône : appelé à la création et à chaque coup cumulé (pas à chaque frame).</summary>
        private void ApplyStyle(bool first)
        {
            style = crit && critStyle != null ? critStyle : stylesByKind[(int)dominant];
            if (style == null) return;

            bool isHeal = style.Kind == DamageFxKind.Heal;
            bool isTower = style.Kind == DamageFxKind.TowerHit;
            small = !crit && !boss && !isHeal && !isTower &&
                    (total < settings.SmallDamageAbsolute || (maxHealth > 0f && total < maxHealth * settings.SmallDamageFraction));

            if (style.Material != null && label.fontSharedMaterial != style.Material) label.fontSharedMaterial = style.Material;
            label.SetText(style.Format, total);
            lastAlpha = -1f;     // force la mise à jour des couleurs au prochain Tick

            // Icône à gauche du chiffre (cachée pour les petits dégâts).
            if (icon != null)
            {
                bool showIcon = settings.ShowIcons && style.Icon != null && !small;
                icon.enabled = showIcon;
                if (showIcon)
                {
                    icon.sprite = style.Icon;
                    float w = label.preferredWidth;
                    float h = label.fontSize * GlyphUnit * 1.05f;     // hauteur d'une lettre en unités locales du texte
                    icon.transform.localScale = Vector3.one * (h / Mathf.Max(0.01f, style.Icon.bounds.size.y));
                    icon.transform.localPosition = new Vector3(-(w * 0.5f + h * 0.55f), 0f, 0f);
                }
            }

            if (burst != null)
            {
                burst.enabled = crit;
                if (crit)
                {
                    burst.transform.localScale = Vector3.one * (label.fontSize * GlyphUnit * 3.2f);
                    burst.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 45f));
                }
            }
        }

        private void UpdateColors(float alpha, bool visible)
        {
            if (style == null) return;
            if (label.enabled != visible) label.enabled = visible;
            if (icon != null) icon.enabled = visible && settings.ShowIcons && style.Icon != null && !small;

            // Éclat du crit : s'éteint vite (chaque frame, un simple SpriteRenderer.color).
            if (burst != null)
            {
                bool showBurst = visible && crit && age < 0.45f;
                burst.enabled = showBurst;
                if (showBurst)
                {
                    Color b = style.TopColor;
                    b.a = alpha * (1f - age / 0.45f);
                    burst.color = b;
                }
            }

            bool flash = flashFrames > 0;
            if (flash) flashFrames--;
            if (Mathf.Approximately(alpha, lastAlpha) && flash == flashShown) return;
            lastAlpha = alpha;
            flashShown = flash;

            // Dégradé vertical (haut clair, bas saturé) + alpha : le TMP n'est reconstruit que quand ça change (coup, flash, fondu).
            Color top = flash ? Color.white : style.TopColor;
            Color bottom = flash ? Color.white : style.BottomColor;
            top.a = bottom.a = alpha;
            label.enableVertexGradient = true;
            label.colorGradient = new VertexGradient(top, top, bottom, bottom);

            if (icon != null && icon.enabled)
            {
                Color c = Color.Lerp(style.TopColor, style.BottomColor, 0.35f) * style.IconColor;     // l'icône prend la teinte du style
                c.a = alpha;
                icon.color = c;
            }
        }
        #endregion
    }
}
