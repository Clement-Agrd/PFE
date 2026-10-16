using UnityEngine;
using UnityEditor;


namespace RealBlend
{
    [System.Serializable]
    public class VertexPainterNoiseModule
    {
        internal const string DirtUndoName = "RealBlend Vertex Paint Dirt";
        public bool showNoiseTools = false;
        public bool noiseAutoPreview = true;

        // --- Dirt Modes ---
        public enum DirtMode
        {
            FullSurface = 0,
            EdgeBand = 1
        }

        public DirtMode dirtMode = DirtMode.FullSurface;

        [Range(0f, 1f)]
        public float dirtAmount = 0.5f;

        [Range(0.01f, 1f)]
        public float dirtSoftness = 0.2f;

        [Range(0f, 1f)]
        public float dirtOpacity = 1.0f;

        [Range(0f, 1f)]
        public float edgeWidth01 = 0.25f;

        // NEW: Toggle to use UVs for edges (Fixes curved objects)
        public bool useUVForEdges = true;

        // --- INTERNAL STATE ---
        [SerializeField] private Vector3 noiseOffset;
        private bool _isPreviewing = false;
        private Color[] _previewBuffer;
        private float[] _noiseValues;

        // --- CONSTANTS ---
        private const int CONST_OCTAVES = 6;
        private const float CONST_PERSISTENCE = 0.55f;
        private const float CONST_LACUNARITY = 2.0f;
        private const float MAX_REAL_SCALE = 0.15f;

        public bool IsPreviewing => _isPreviewing;

        public void DrawGUI(
            VertexPainter_PaintTab owner,
            MeshFilter mf,
            Mesh workingMesh,
            ref Color[] storedColors,
            ref Vector3[] originalVertices)
        {
            if (workingMesh == null) return;

            showNoiseTools = EditorGUILayout.Foldout(showNoiseTools, "Procedural Dirt / Grime", true);
            if (!showNoiseTools)
            {
                if (_isPreviewing) ClearPreview(workingMesh, storedColors);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Dirt Generator", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            dirtMode = (DirtMode)EditorGUILayout.EnumPopup("Mode", dirtMode);

            // Contextual Help
            if (dirtMode == DirtMode.EdgeBand)
            {
                EditorGUILayout.HelpBox("Edge Mode paints near the borders.", MessageType.None);
                // New Toggle
                useUVForEdges = EditorGUILayout.Toggle(new GUIContent("Use UVs for Edge", "Best for Curved Objects/Pipes. Uses the UV Map borders instead of World Position."), useUVForEdges);
            }

            dirtAmount = EditorGUILayout.Slider("Coverage", dirtAmount, 0f, 1f);
            dirtSoftness = EditorGUILayout.Slider("Edge Softness", dirtSoftness, 0.01f, 1f);
            dirtOpacity = EditorGUILayout.Slider("Opacity", dirtOpacity, 0f, 1f);

            if (dirtMode == DirtMode.EdgeBand)
            {
                edgeWidth01 = EditorGUILayout.Slider("Edge Width", edgeWidth01, 0f, 1f);
            }

            bool changed = EditorGUI.EndChangeCheck();

            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("🎲 Randomize Pattern"))
            {
                noiseOffset = new Vector3(
                    Random.Range(-5000f, 5000f),
                    Random.Range(-5000f, 5000f),
                    Random.Range(-5000f, 5000f)
                );
                if (_isPreviewing)
                    GeneratePreview(owner, mf, workingMesh, storedColors, originalVertices);
            }

            if (GUILayout.Button(_isPreviewing ? "Update Preview" : "👁️ Preview"))
            {
                GeneratePreview(owner, mf, workingMesh, storedColors, originalVertices);
            }

            Color defaultBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
            if (GUILayout.Button("✓ Apply"))
            {
                ApplyToMesh(owner, mf, workingMesh, ref storedColors, originalVertices);
            }
            GUI.backgroundColor = defaultBg;

            EditorGUILayout.EndHorizontal();

            if (_isPreviewing)
            {
                if (GUILayout.Button("Clear Preview"))
                {
                    ClearPreview(workingMesh, storedColors);
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.EndVertical();

            if (changed && noiseAutoPreview)
            {
                GeneratePreview(owner, mf, workingMesh, storedColors, originalVertices);
            }
        }

        public void ClearPreview(Mesh workingMesh, Color[] storedColors)
        {
            if (!_isPreviewing) return;
            if (workingMesh == null || storedColors == null) return;
            workingMesh.colors = storedColors;
            workingMesh.UploadMeshData(false);
            _isPreviewing = false;
        }

        internal void AbandonPreview()
        {
            _isPreviewing = false;
        }

        internal void RestoreCommittedColors(
            Mesh workingMesh,
            Color[] storedColors)
        {
            if (workingMesh != null && storedColors != null)
            {
                workingMesh.colors = storedColors;
                workingMesh.UploadMeshData(false);
            }
            _isPreviewing = false;
        }

        void GeneratePreview(
            VertexPainter_PaintTab owner,
            MeshFilter mf,
            Mesh workingMesh,
            Color[] storedColors,
            Vector3[] originalVertices)
        {
            if (owner == null)
                return;
            owner.PrepareForProceduralPreview();
            if (!owner.EnsurePaintMeshEditableForChange())
                return;
            mf = owner.CurrentMeshFilter;
            workingMesh = owner.CurrentWorkingMesh;
            storedColors = owner.CurrentStoredColors;
            if (workingMesh == null || storedColors == null || mf == null) return;

            // An edit lease can reconcile geometry without changing vertex count.
            originalVertices = workingMesh.vertices;

            if (_previewBuffer == null || _previewBuffer.Length != originalVertices.Length)
                _previewBuffer = new Color[originalVertices.Length];

            System.Array.Copy(storedColors, _previewBuffer, storedColors.Length);

            // Pass the Mesh to get UVs
            CalculateNoise(owner, mf, workingMesh, originalVertices, _previewBuffer);
            workingMesh.colors = _previewBuffer;
            workingMesh.UploadMeshData(false);
            _isPreviewing = true;
            SceneView.RepaintAll();
        }

        void ApplyToMesh(
            VertexPainter_PaintTab owner,
            MeshFilter mf,
            Mesh workingMesh,
            ref Color[] storedColors,
            Vector3[] originalVertices)
        {
            if (owner == null)
                return;

            // Never let a displayed preview become the baseline if acquiring
            // the edit lease also has to reconcile or clone the mesh.
            owner.PrepareForProceduralPreview();
            if (!owner.EnsurePaintMeshEditableForChange())
                return;
            mf = owner.CurrentMeshFilter;
            workingMesh = owner.CurrentWorkingMesh;
            storedColors = owner.CurrentStoredColors;
            if (workingMesh == null || storedColors == null || mf == null) return;

            originalVertices = workingMesh.vertices;

            Color[] dirtColors = (Color[])storedColors.Clone();
            CalculateNoise(owner, mf, workingMesh, originalVertices, dirtColors);
            if (ColorsEqual(storedColors, dirtColors))
                return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(DirtUndoName);
            VertexPaintColorUndoState undoState =
                VertexPainter_PaintTab.PrepareColorUndoState(
                    mf,
                    workingMesh,
                    owner.CurrentStorage,
                    DirtUndoName);
            storedColors = dirtColors;
            workingMesh.colors = storedColors;
            workingMesh.UploadMeshData(false);

            if (owner.CurrentStorage != null)
            {
                owner.CurrentStorage.SavePaintedColors(workingMesh, storedColors);
                RealBlendProBuilderColorBridge.SyncPaintedColorsToProBuilder(
                    mf,
                    owner.CurrentStorage,
                    storedColors);
                EditorUtility.SetDirty(owner.CurrentStorage);
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    owner.CurrentStorage);
            }
            VertexPainter_PaintTab.CommitColorUndoState(undoState, storedColors);
        }

        private static bool ColorsEqual(Color[] left, Color[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        void CalculateNoise(
            VertexPainter_PaintTab owner,
            MeshFilter mf,
            Mesh mesh, // Added Mesh param to access UVs
            Vector3[] verts,
            Color[] targetColors)
        {
            if (verts == null || targetColors == null || verts.Length != targetColors.Length) return;
            float coverage = Mathf.Clamp01(dirtAmount);
            float opacity = Mathf.Clamp01(dirtOpacity);
            if (coverage <= 0f || opacity <= 0f ||
                (dirtMode == DirtMode.EdgeBand && edgeWidth01 <= 0f))
                return;

            // Get UVs if we need them
            Vector2[] uvs = (dirtMode == DirtMode.EdgeBand && useUVForEdges) ? mesh.uv : null;
            if (dirtMode == DirtMode.EdgeBand && useUVForEdges && (uvs == null || uvs.Length != verts.Length))
            {
                // Fallback if no UVs
                uvs = null;
            }

            bool modified = false;
            float actualFreq = MAX_REAL_SCALE;
            float softness = Mathf.Clamp(dirtSoftness, 0.01f, 1f);

            // FBM occupies a narrow, mesh-dependent range. Normalize the fixed
            // field, not the slider result, so low Coverage still reveals the
            // same pattern and changing Softness cannot move its peaks.
            if (_noiseValues == null || _noiseValues.Length != verts.Length)
                _noiseValues = new float[verts.Length];
            float noiseMin = float.MaxValue, noiseMax = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = verts[i] * actualFreq + noiseOffset;
                Vector3 warp = new Vector3(
                    Mathf.PerlinNoise(p.x * 0.5f, p.y * 0.5f),
                    Mathf.PerlinNoise(p.y * 0.5f, p.z * 0.5f),
                    Mathf.PerlinNoise(p.z * 0.5f, p.x * 0.5f)) * 2f;
                float value = GetFBM(p + warp);
                _noiseValues[i] = value;
                noiseMin = Mathf.Min(noiseMin, value);
                noiseMax = Mathf.Max(noiseMax, value);
            }
            float noiseRange = noiseMax - noiseMin;

            // --- BOUNDS CALCULATION ---
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            if (dirtMode == DirtMode.EdgeBand)
            {
                if (useUVForEdges && uvs != null)
                {
                    // Calculate UV Bounds
                    for (int i = 0; i < uvs.Length; i++)
                    {
                        Vector2 uv = uvs[i];
                        if (uv.x < minX) minX = uv.x;
                        if (uv.x > maxX) maxX = uv.x;
                        if (uv.y < minZ) minZ = uv.y; // Map V to Z logic
                        if (uv.y > maxZ) maxZ = uv.y;
                    }
                }
                else
                {
                    // Original Position Bounds
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 v = verts[i];
                        if (v.x < minX) minX = v.x;
                        if (v.x > maxX) maxX = v.x;
                        if (v.z < minZ) minZ = v.z;
                        if (v.z > maxZ) maxZ = v.z;
                    }
                }
            }

            float edgeWidth = 0f;
            if (dirtMode == DirtMode.EdgeBand)
            {
                float sizeX = maxX - minX;
                float sizeZ = maxZ - minZ;
                float minSize = Mathf.Max(Mathf.Min(sizeX, sizeZ), 0.0001f);
                edgeWidth = edgeWidth01 * (minSize * 0.5f);
            }

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];

                float n = noiseRange > 0.000001f
                    ? Mathf.Clamp01((_noiseValues[i] - noiseMin) / noiseRange)
                    : 0f;
                // The ramp reaches zero and one continuously at Coverage's
                // endpoints. A constant field simply fades with Coverage.
                float mask = coverage >= 1f ? 1f
                    : noiseRange > 0.000001f
                        ? Mathf.Clamp01((coverage - (1f - n) * (1f - softness)) / softness)
                        : coverage;

                // --- EDGE BAND LOGIC ---
                if (dirtMode == DirtMode.EdgeBand && edgeWidth > 0f)
                {
                    float distToLeft, distToRight, distToBottom, distToTop;

                    if (useUVForEdges && uvs != null)
                    {
                        // Use UV Coordinates for distance
                        Vector2 uv = uvs[i];
                        distToLeft = uv.x - minX;
                        distToRight = maxX - uv.x;
                        distToBottom = uv.y - minZ; // V maps to Z logic
                        distToTop = maxZ - uv.y;
                    }
                    else
                    {
                        // Use World Positions
                        distToLeft = v.x - minX;
                        distToRight = maxX - v.x;
                        distToBottom = v.z - minZ;
                        distToTop = maxZ - v.z;
                    }

                    float distEdge = Mathf.Min(
                        Mathf.Min(distToLeft, distToRight),
                        Mathf.Min(distToBottom, distToTop)
                    );

                    float edgeFactor = Mathf.Clamp01(1f - (distEdge / edgeWidth));
                    edgeFactor = Mathf.SmoothStep(0f, 1f, edgeFactor);

                    mask *= edgeFactor;
                }

                mask *= opacity;

                if (mask <= 0f) continue;

                owner.ApplyLayerBlend(i, mask, targetColors, targetColors, false, ref modified);
            }
        }

        float GetFBM(Vector3 p)
        {
            float sum = 0f;
            float amp = 1f;
            float max = 0f;

            for (int i = 0; i < CONST_OCTAVES; i++)
            {
                float n = (Mathf.PerlinNoise(p.x, p.y) +
                           Mathf.PerlinNoise(p.x + 17.1f, p.z + 31.7f) +
                           Mathf.PerlinNoise(p.y + 102.3f, p.z + 13.1f)) / 3.0f;

                sum += n * amp;
                max += amp;
                amp *= CONST_PERSISTENCE;
                p *= CONST_LACUNARITY;
            }

            return sum / Mathf.Max(max, 0.0001f);
        }
    }
}
