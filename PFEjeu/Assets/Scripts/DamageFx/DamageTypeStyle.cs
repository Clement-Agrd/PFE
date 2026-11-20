using UnityEngine;

namespace DamageFx
{
    /// <summary>
    /// Style visuel d'un type de dégâts : couleurs (dégradé + contour), icône, échelle, durée, courbe d'animation, priorité.
    /// Un asset par DamageFxKind (créés par Tools > Damage FX > 1 Créer les assets par défaut).
    /// </summary>
    [CreateAssetMenu(fileName = "DamageStyle", menuName = "Damage FX/Damage Type Style")]
    public sealed class DamageTypeStyle : ScriptableObject
    {
        [Header("Identité")]
        [SerializeField] private DamageFxKind kind;
        [SerializeField, Tooltip("Texte devant le chiffre (ex. '+' pour les soins).")] private string prefix = "";
        [SerializeField, Tooltip("Texte derrière le chiffre (ex. '!' pour un crit).")] private string suffix = "";

        [Header("Couleurs")]
        [SerializeField, Tooltip("Haut de la lettre (clair).")] private Color topColor = Color.white;
        [SerializeField, Tooltip("Bas de la lettre (saturé).")] private Color bottomColor = new Color(0.9f, 0.85f, 0.75f);
        [SerializeField, Tooltip("Teinte du contour (sombre, change selon le type pour garder le contraste).")] private Color outlineColor = new Color(0.08f, 0.05f, 0.03f);
        [SerializeField, Tooltip("Matériau TMP du style (contour, ombre, glow). Créé par l'outil Editor.")] private Material material;
        [SerializeField, Tooltip("Icône (forme) affichée à côté du chiffre : jamais la couleur seule.")] private Sprite icon;
        [SerializeField] private Color iconColor = Color.white;

        [Header("Taille & durée")]
        [SerializeField, Min(0.1f)] private float scale = 1f;
        [SerializeField, Min(0.1f)] private float lifeMultiplier = 1f;
        [SerializeField, Min(0f)] private float riseMultiplier = 1f;
        [SerializeField, Tooltip("Apparition avec dépassement (0 -> 1,3 -> 1). Évaluée sur 0..1 pendant popDuration.")]
        private AnimationCurve popCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.55f, 1.3f), new Keyframe(1f, 1f));

        [Header("Priorité (plafond d'affichage)")]
        [SerializeField, Tooltip("Plus haut = gardé plus longtemps quand le plafond est atteint. Normal 10, soin 20, tour 30.")]
        private int priority = 10;

        private string format;

        public DamageFxKind Kind => kind;
        public Color TopColor => topColor;
        public Color BottomColor => bottomColor;
        public Color OutlineColor => outlineColor;
        public Material Material => material;
        public Sprite Icon => icon;
        public Color IconColor => iconColor;
        public float Scale => scale;
        public float LifeMultiplier => lifeMultiplier;
        public float RiseMultiplier => riseMultiplier;
        public AnimationCurve PopCurve => popCurve;
        public int Priority => priority;

        /// <summary>Format TMP prêt à l'emploi ("+{0:0}!") : construit une seule fois, aucune allocation ensuite.</summary>
        public string Format => format ??= prefix + "{0:0}" + suffix;

        private void OnValidate() { format = null; }

#if UNITY_EDITOR
        /// <summary>Utilisé par l'outil Editor pour remplir un style par défaut.</summary>
        public void EditorInit(DamageFxKind k, Color top, Color bottom, Color outline, Material mat, Sprite ico, float sc, float life, float rise, int prio, string pre = "", string suf = "")
        {
            kind = k; topColor = top; bottomColor = bottom; outlineColor = outline; material = mat; icon = ico;
            scale = sc; lifeMultiplier = life; riseMultiplier = rise; priority = prio; prefix = pre; suffix = suf;
            format = null;
        }
#endif
    }
}
