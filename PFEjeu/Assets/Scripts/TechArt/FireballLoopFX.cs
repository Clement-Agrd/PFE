using UnityEngine;

/// <summary>
/// Anime en boucle le modèle FireballVFX : bouillonnement des couches de feu, vacillement des
/// flammes, scintillement de la lumière, dérive des braises et de la fumée vers l'arrière (-Z).
/// Attend les groupes enfants "ball", "flames", "smoke" et "embers" (chacun contenant des pivots).
/// </summary>
public class FireballLoopFX : MonoBehaviour
{
    [Header("Feu")]
    [Range(0f, 90f)] [SerializeField] private float ballSpinSpeed = 25f;
    [Range(0f, 0.5f)] [SerializeField] private float pulseAmount = 0.12f;
    [Range(0.5f, 10f)] [SerializeField] private float pulseFrequency = 3f;

    [Header("Flammes")]
    [Range(0f, 1f)] [SerializeField] private float flameFlicker = 0.35f;
    [Range(0.5f, 20f)] [SerializeField] private float flameNoiseSpeed = 6f;

    [Header("Lumière")]
    [SerializeField] private Light fireLight;
    [Min(0f)] [SerializeField] private float lightBaseIntensity = 3f;
    [Range(0f, 1f)] [SerializeField] private float lightFlicker = 0.3f;

    [Header("Traînée (fumée / braises)")]
    [Min(0.1f)] [SerializeField] private float smokeDriftSpeed = 1.2f;
    [Min(0.1f)] [SerializeField] private float emberDriftSpeed = 2f;
    [Min(0.1f)] [SerializeField] private float driftLength = 1.6f;

    private Transform[] _ball, _flames, _smoke, _embers;
    private Vector3[] _ballScale, _flameScale, _smokeScale;
    private Vector3[] _smokePos, _emberPos;
    private float[] _seed;

    private void Awake()
    {
        _ball = Collect("ball");
        _flames = Collect("flames");
        _smoke = Collect("smoke");
        _embers = Collect("embers");

        _ballScale = Scales(_ball);
        _flameScale = Scales(_flames);
        _smokeScale = Scales(_smoke);
        _smokePos = LocalPositions(_smoke);
        _emberPos = LocalPositions(_embers);

        _seed = new float[Mathf.Max(_ball.Length, _flames.Length, _smoke.Length, _embers.Length)];
        for (int i = 0; i < _seed.Length; i++) _seed[i] = Random.value * 100f;

        if (fireLight == null) fireLight = GetComponentInChildren<Light>();
        if (fireLight == null) Debug.LogWarning("[FireballLoopFX] Aucune Light trouvée.", this);
    }

    private Transform[] Collect(string groupName)
    {
        Transform group = transform.Find("Visual/" + groupName);
        if (group == null)
        {
            Debug.LogWarning($"[FireballLoopFX] Groupe '{groupName}' introuvable.", this);
            return new Transform[0];
        }
        var list = new Transform[group.childCount];
        for (int i = 0; i < list.Length; i++) list[i] = group.GetChild(i);
        return list;
    }

    private static Vector3[] Scales(Transform[] ts)
    {
        var s = new Vector3[ts.Length];
        for (int i = 0; i < ts.Length; i++) s[i] = ts[i].localScale;
        return s;
    }

    private static Vector3[] LocalPositions(Transform[] ts)
    {
        var p = new Vector3[ts.Length];
        for (int i = 0; i < ts.Length; i++) p[i] = ts[i].localPosition;
        return p;
    }

    private void Update()
    {
        float t = Time.time;

        // Couches de feu : chaque couche pulse à sa propre fréquence (bouillonnement).
        for (int i = 0; i < _ball.Length; i++)
        {
            float pulse = 1f + Mathf.Sin(t * pulseFrequency * (1f + i * 0.45f) + _seed[i]) * pulseAmount;
            _ball[i].localScale = _ballScale[i] * pulse;
            _ball[i].Rotate(0f, 0f, ballSpinSpeed * (i % 2 == 0 ? 1f : -1f) * Time.deltaTime, Space.Self);
        }

        // Flammes : étirement vacillant au bruit de Perlin.
        for (int i = 0; i < _flames.Length; i++)
        {
            float n = Mathf.PerlinNoise(_seed[i], t * flameNoiseSpeed * 0.2f) * 2f - 1f;
            Vector3 s = _flameScale[i];
            s.z *= 1f + n * flameFlicker;
            _flames[i].localScale = s;
        }

        if (fireLight != null)
        {
            float n = Mathf.PerlinNoise(t * 9f, 0.5f) * 2f - 1f;
            fireLight.intensity = lightBaseIntensity * (1f + n * lightFlicker);
        }

        Drift(_smoke, _smokePos, smokeDriftSpeed, t, _smokeScale);
        Drift(_embers, _emberPos, emberDriftSpeed, t, null);
    }

    /// <summary>Fait dériver les pivots vers -Z (espace local de la racine) et les fait reboucler.</summary>
    private void Drift(Transform[] items, Vector3[] basePos, float speed, float t, Vector3[] baseScale)
    {
        for (int i = 0; i < items.Length; i++)
        {
            float phase = Mathf.Repeat(t * speed / driftLength + _seed[i], 1f);
            items[i].localPosition = basePos[i] + Vector3.back * (phase * driftLength);

            // Fondu par réduction d'échelle au début et à la fin du cycle.
            float fade = Mathf.Sin(phase * Mathf.PI);
            Vector3 s = baseScale != null ? baseScale[i] : items[i].localScale;
            if (baseScale != null) items[i].localScale = s * Mathf.Lerp(0.6f, 1.2f, phase) * fade;
            else items[i].localScale = Vector3.one * Mathf.Max(0.05f, fade);
        }
    }
}
