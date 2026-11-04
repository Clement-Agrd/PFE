using UnityEngine;
using UnityEngine.Rendering;

namespace AllyFx
{
    /// <summary>
    /// Ruban de slash généré entre deux points de la lame (style "swoosh" stylisé).
    ///
    /// Principe :
    ///   - chaque frame d'émission, on mémorise la paire (intérieur de lame, pointe) en espace monde ;
    ///   - on reconstruit un mesh en bandelette dont UV.x = âge (0 = neuf, 1 = expiré)
    ///     et UV.y = position sur la lame (0 = intérieur, 1 = pointe) ;
    ///   - le mesh est dessiné par Graphics.DrawMesh : pas de parent mobile, pas de MeshRenderer.
    ///
    /// Zéro allocation par frame (buffers préalloués), et le script est désactivé au repos.
    /// À utiliser avec le shader "Ally/FX/SlashTrail".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlashRibbonTrail : MonoBehaviour
    {
        #region Inspector
        [Header("Lame")]
        [Tooltip("Point de la lame le plus proche de la garde (ou la main).")]
        [SerializeField] private Transform bladeBase;
        [Tooltip("Pointe de la lame.")]
        [SerializeField] private Transform bladeTip;
        [Tooltip("Où commence le ruban sur la lame (0 = garde, 1 = pointe). Plus haut = ruban plus fin.")]
        [SerializeField, Range(0f, 0.95f)] private float innerStart = 0.3f;

        [Header("Ruban")]
        [SerializeField] private Material trailMaterial;
        [SerializeField, Min(0.05f)] private float lifetime = 0.35f;
        [Tooltip("Distance minimale (unités monde) entre deux échantillons de la pointe.")]
        [SerializeField, Min(0.001f)] private float minSampleDistance = 0.03f;
        [Tooltip("Nombre max d'échantillons mémorisés. Fixé à l'Awake.")]
        [SerializeField, Range(8, 128)] private int maxSamples = 48;

        [Header("Douceur")]
        [Tooltip("Subdivisions Catmull-Rom entre deux échantillons : lisse les arêtes anguleuses des coups rapides.")]
        [SerializeField, Range(1, 8)] private int subdivisions = 4;
        [Tooltip("Largeur qui s effile sur la tête du ruban (âge 0 → valeur). 0 = tête carrée.")]
        [SerializeField, Range(0f, 0.5f)] private float headTaper = 0.15f;
        [Tooltip("Largeur qui s effile vers la queue (à partir de cet âge). 1 = pas d effilement.")]
        [SerializeField, Range(0.1f, 1f)] private float tailTaperStart = 0.45f;
        #endregion

        #region Etat
        private Mesh ribbonMesh;
        private Vector3[] innerSamples;
        private Vector3[] tipSamples;
        private float[] sampleTimes;
        private Vector3[] vertices;
        private Vector2[] uvs;
        private int[] indices;
        private Vector3[] ptInner;
        private Vector3[] ptTip;
        private float[] ptAge;

        private int oldestIndex;
        private int sampleCount;
        private bool isEmitting;
        private bool isConfigured;
        #endregion

        #region Cycle Unity
        private void Awake()
        {
            isConfigured = bladeBase != null && bladeTip != null && trailMaterial != null;

            if (!isConfigured)
            {
                Debug.LogError(
                    $"[{nameof(SlashRibbonTrail)}] Référence manquante (bladeBase / bladeTip / trailMaterial) sur '{name}'.",
                    this);
                enabled = false;
                return;
            }

            AllocateBuffers();

            // LateUpdate inutile au repos : réactivé uniquement pendant un slash et son estompage.
            enabled = false;
        }

        // LateUpdate : l'Animator a déjà posé les os, la pointe est à sa position finale de la frame.
        private void LateUpdate()
        {
            float now = Time.time;

            if (isEmitting)
            {
                TryAddSample(now);
            }

            RemoveExpiredSamples(now);

            if (!isEmitting && sampleCount == 0)
            {
                enabled = false;
                return;
            }

            DrawRibbon(now);
        }

        private void OnDestroy()
        {
            if (ribbonMesh != null)
            {
                Destroy(ribbonMesh);
            }
        }
        #endregion

        #region API
        /// <summary>true = on émet le ruban ; false = on arrête et le ruban s'estompe tout seul.</summary>
        public void SetEmitting(bool emit)
        {
            if (!isConfigured || isEmitting == emit) return;

            // Dernier échantillon à la pose finale de la lame : évite que la tête du ruban disparaisse d un coup.
            if (!emit) TryAddSample(Time.time);

            isEmitting = emit;

            if (emit)
            {
                ClearSamples();
                enabled = true;
            }
        }

        /// <summary>Coupe tout immédiatement, sans estompage.</summary>
        public void Cancel()
        {
            isEmitting = false;
            ClearSamples();
            enabled = false;
        }
        #endregion

        #region Echantillons
        private void AllocateBuffers()
        {
            int capacity = maxSamples;
            int pairCapacity = (capacity + 1) * subdivisions + 1; // +1 : paire "live" ; x subdivisions pour le lissage
            ptInner = new Vector3[capacity + 1];
            ptTip = new Vector3[capacity + 1];
            ptAge = new float[capacity + 1];

            innerSamples = new Vector3[capacity];
            tipSamples = new Vector3[capacity];
            sampleTimes = new float[capacity];

            vertices = new Vector3[pairCapacity * 2];
            uvs = new Vector2[pairCapacity * 2];
            indices = new int[(pairCapacity - 1) * 6];

            // Triangles constants : seule leur longueur utilisée change à chaque frame.
            for (int quad = 0; quad < pairCapacity - 1; quad++)
            {
                int v = quad * 2;
                int i = quad * 6;
                indices[i] = v;
                indices[i + 1] = v + 1;
                indices[i + 2] = v + 2;
                indices[i + 3] = v + 1;
                indices[i + 4] = v + 3;
                indices[i + 5] = v + 2;
            }

            ribbonMesh = new Mesh { name = "SlashRibbon" };
            ribbonMesh.MarkDynamic();
        }

        private void ClearSamples()
        {
            oldestIndex = 0;
            sampleCount = 0;
        }

        private int WrapIndex(int index)
        {
            return index % innerSamples.Length;
        }

        private void GetBladePoints(out Vector3 inner, out Vector3 tip)
        {
            tip = bladeTip.position;
            inner = Vector3.Lerp(bladeBase.position, tip, innerStart);
        }

        private void TryAddSample(float now)
        {
            GetBladePoints(out Vector3 inner, out Vector3 tip);

            if (sampleCount > 0)
            {
                int lastIndex = WrapIndex(oldestIndex + sampleCount - 1);
                float minSqrDistance = minSampleDistance * minSampleDistance;

                if ((tip - tipSamples[lastIndex]).sqrMagnitude < minSqrDistance) return;
            }

            // Buffer plein : on écrase le plus ancien.
            if (sampleCount == innerSamples.Length)
            {
                oldestIndex = WrapIndex(oldestIndex + 1);
                sampleCount--;
            }

            int index = WrapIndex(oldestIndex + sampleCount);
            innerSamples[index] = inner;
            tipSamples[index] = tip;
            sampleTimes[index] = now;
            sampleCount++;
        }

        private void RemoveExpiredSamples(float now)
        {
            while (sampleCount > 0 && now - sampleTimes[oldestIndex] >= lifetime)
            {
                oldestIndex = WrapIndex(oldestIndex + 1);
                sampleCount--;
            }
        }
        #endregion

        #region Rendu
        private void DrawRibbon(float now)
        {
            // 1) Points de contrôle (échantillons + paire "live"), du plus ancien au plus récent.
            int pointCount = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                int s = WrapIndex(oldestIndex + i);
                ptInner[pointCount] = innerSamples[s];
                ptTip[pointCount] = tipSamples[s];
                ptAge[pointCount] = Mathf.Clamp01((now - sampleTimes[s]) / lifetime);
                pointCount++;
            }

            // Paire "live" : la tête du ruban colle à la lame même quand elle ralentit.
            if (isEmitting)
            {
                GetBladePoints(out Vector3 liveInner, out Vector3 liveTip);
                ptInner[pointCount] = liveInner;
                ptTip[pointCount] = liveTip;
                ptAge[pointCount] = 0f;
                pointCount++;
            }

            if (pointCount < 2) return;

            // 2) Sous-division Catmull-Rom : des courbes lisses au lieu de cordes anguleuses.
            int vertexIndex = 0;
            int segments = pointCount - 1;

            for (int seg = 0; seg < segments; seg++)
            {
                int p0 = Mathf.Max(seg - 1, 0);
                int p1 = seg;
                int p2 = seg + 1;
                int p3 = Mathf.Min(seg + 2, pointCount - 1);

                for (int k = 0; k < subdivisions; k++)
                {
                    float t = k / (float)subdivisions;
                    WriteSmoothed(ref vertexIndex, p0, p1, p2, p3, t);
                }
            }

            WriteSmoothed(ref vertexIndex, Mathf.Max(segments - 1, 0), segments, segments, segments, 0f); // dernier point exact

            int pairCount = vertexIndex / 2;
            if (pairCount < 2) return;

            ribbonMesh.Clear();
            ribbonMesh.SetVertices(vertices, 0, vertexIndex);
            ribbonMesh.SetUVs(0, uvs, 0, vertexIndex);
            ribbonMesh.SetTriangles(indices, 0, (pairCount - 1) * 6, 0, false);
            ribbonMesh.RecalculateBounds();

            Graphics.DrawMesh(
                ribbonMesh,
                Matrix4x4.identity, // sommets déjà en espace monde
                trailMaterial,
                gameObject.layer,
                null,
                0,
                null,
                ShadowCastingMode.Off,
                false);
        }

        private static Vector3 CatmullRom(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (-a + 3f * b - 3f * c + d) * t3);
        }

        // Écrit une paire de sommets interpolée + effilée (têtes et queues pointues, plus de bords carrés).
        private void WriteSmoothed(ref int vertexIndex, int p0, int p1, int p2, int p3, float t)
        {
            Vector3 inner = CatmullRom(ptInner[p0], ptInner[p1], ptInner[p2], ptInner[p3], t);
            Vector3 tip = CatmullRom(ptTip[p0], ptTip[p1], ptTip[p2], ptTip[p3], t);
            float age01 = Mathf.Lerp(ptAge[p1], ptAge[p2], t);

            // Effilement de la largeur : tête (âge ~0) et queue (âge -> 1) se resserrent vers l'axe central.
            float width = 1f;
            if (headTaper > 0f) width *= Mathf.SmoothStep(0f, 1f, age01 / headTaper);
            if (tailTaperStart < 1f) width *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tailTaperStart, 1f, age01));

            Vector3 center = (inner + tip) * 0.5f;
            Vector3 half = (tip - inner) * 0.5f * width;
            WritePair(ref vertexIndex, center - half, center + half, age01);
        }

        private void WritePair(ref int vertexIndex, Vector3 inner, Vector3 tip, float age01)
        {
            vertices[vertexIndex] = inner;
            uvs[vertexIndex] = new Vector2(age01, 0f);
            vertexIndex++;

            vertices[vertexIndex] = tip;
            uvs[vertexIndex] = new Vector2(age01, 1f);
            vertexIndex++;
        }
        #endregion
    }
}
