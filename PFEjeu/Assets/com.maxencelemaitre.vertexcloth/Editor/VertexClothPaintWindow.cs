using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VertexCloth.EditorTools
{
    /// <summary>
    /// Retouche manuelle des poids (0 = fixe, 1 = libre) et bake d'un seul vêtement.
    /// Pour un personnage complet, utiliser Tools > Vertex Cloth > Character Setup.
    /// </summary>
    public sealed class VertexClothPaintWindow : EditorWindow
    {
        private enum BrushMode { Paint, Smooth, Island }

        private const int MaxHistory = 20;
        private const float PreviewOffsetFactor = 0.0005f;

        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private VertexClothData dataAsset;
        [SerializeField] private VertexClothSettings settingsAsset;
        [SerializeField] private VertexClothPreset preset;

        // Pinceau
        private bool paintEnabled = true;
        private BrushMode mode = BrushMode.Paint;
        private float paintValue = 1f;
        private float brushRadius = 0.08f;   // unités monde
        private float brushStrength = 0.5f;
        private float brushHardness = 0.3f;
        private bool mirrorX = true;
        private bool facingOnly = true;
        private float overlayOpacity = 0.6f;

        // Dégradé de hauteur manuel (espace local du mesh)
        private float pinHeight;
        private float freeHeight;

        // Données de travail
        private ClothTopology topology;
        private Vector3[] meshVertices;
        private float[] freedom;
        private readonly List<float[]> history = new List<float[]>();

        // Aperçu
        private Mesh previewMesh;
        private Material overlayMaterial;
        private Color[] previewColors;

        // Survol
        private bool hasHover;
        private int hoverTriangle;
        private Vector3 hoverLocalPoint;
        private Vector3 hoverLocalNormal;

        private Vector2 scroll;

        private Mesh SourceMesh => targetRenderer != null ? VertexClothEditorUtility.GetMesh(targetRenderer) : null;

        [MenuItem("Tools/Vertex Cloth/Paint Window")]
        private static void Open()
        {
            GetWindow<VertexClothPaintWindow>("Vertex Cloth Paint");
        }

        #region Lifecycle
        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            if (targetRenderer != null)
                Load();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            DestroyPreview();
            if (overlayMaterial != null)
                DestroyImmediate(overlayMaterial);
        }
        #endregion

        #region GUI
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUI.BeginChangeCheck();
            targetRenderer = (Renderer)EditorGUILayout.ObjectField("Cible (Renderer)", targetRenderer, typeof(Renderer), true);
            if (EditorGUI.EndChangeCheck())
            {
                dataAsset = null;
                Load();
            }

            if (GUILayout.Button("Utiliser la sélection"))
            {
                targetRenderer = FindRenderer(Selection.activeGameObject);
                dataAsset = null;
                Load();
            }

            dataAsset = (VertexClothData)EditorGUILayout.ObjectField("Données (asset)", dataAsset, typeof(VertexClothData), false);
            settingsAsset = (VertexClothSettings)EditorGUILayout.ObjectField("Réglages (asset)", settingsAsset, typeof(VertexClothSettings), false);
            preset = (VertexClothPreset)EditorGUILayout.ObjectField("Preset", preset, typeof(VertexClothPreset), false);

            Mesh mesh = SourceMesh;
            if (targetRenderer == null || mesh == null)
            {
                EditorGUILayout.HelpBox("Sélectionne un objet avec un MeshRenderer ou un SkinnedMeshRenderer.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (!mesh.isReadable)
            {
                EditorGUILayout.HelpBox("Active Read/Write dans l'import du modèle (Model > Read/Write), puis recharge.", MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (topology == null)
            {
                if (GUILayout.Button("Charger le mesh"))
                    Load();
                EditorGUILayout.EndScrollView();
                return;
            }

            string skinnedNote = targetRenderer is SkinnedMeshRenderer
                ? "\nSkinnedMeshRenderer : garde le personnage en pose de bind (T-pose) pendant la peinture."
                : string.Empty;

            EditorGUILayout.HelpBox(
                $"{topology.ParticleCount} particules (soudées) · {topology.IslandCount} îlot(s)\n" +
                "Bleu = fixe · Rouge = libre. Alt + souris pour orbiter." + skinnedNote, MessageType.None);

            DrawBrushSection();
            DrawFillSection();
            DrawActionsSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawBrushSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pinceau", EditorStyles.boldLabel);

            paintEnabled = EditorGUILayout.Toggle("Peinture active", paintEnabled);
            mode = (BrushMode)EditorGUILayout.EnumPopup("Mode", mode);
            paintValue = EditorGUILayout.Slider("Valeur (0 fixe · 1 libre)", paintValue, 0f, 1f);
            brushRadius = EditorGUILayout.Slider("Rayon (monde)", brushRadius, 0.005f, 1f);
            brushStrength = EditorGUILayout.Slider("Force", brushStrength, 0.01f, 1f);
            brushHardness = EditorGUILayout.Slider("Dureté", brushHardness, 0f, 1f);
            mirrorX = EditorGUILayout.Toggle("Miroir X (local)", mirrorX);
            facingOnly = EditorGUILayout.Toggle("Face visible seulement", facingOnly);

            EditorGUI.BeginChangeCheck();
            overlayOpacity = EditorGUILayout.Slider("Opacité overlay", overlayOpacity, 0.05f, 1f);
            if (EditorGUI.EndChangeCheck())
                RefreshPreviewColors();

            if (mode == BrushMode.Island)
                EditorGUILayout.HelpBox("Mode Île : un clic applique la valeur à tout le morceau de mesh connecté.", MessageType.None);
        }

        private void DrawFillSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Remplissage", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(preset == null))
            {
                if (GUILayout.Button("Poids automatiques depuis le preset"))
                    ApplyPreset();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Tout fixe")) FillAll(0f);
                if (GUILayout.Button("Tout libre")) FillAll(1f);
                if (GUILayout.Button("Inverser")) InvertAll();
            }

            pinHeight = EditorGUILayout.FloatField("Hauteur fixe (Y local)", pinHeight);
            freeHeight = EditorGUILayout.FloatField("Hauteur libre (Y local)", freeHeight);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Auto depuis bounds")) AutoHeights();
                if (GUILayout.Button("Appliquer dégradé")) ApplyHeightGradient();
            }
        }

        private void DrawActionsSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(history.Count == 0))
            {
                if (GUILayout.Button("Annuler le dernier trait"))
                    UndoLast();
            }

            if (GUILayout.Button("Bake (sauvegarder l'asset)"))
                Bake();

            if (GUILayout.Button("Bake + configurer le composant"))
            {
                if (Bake())
                {
                    VertexClothEditorUtility.AttachSimulator(targetRenderer, dataAsset, settingsAsset);
                    Selection.activeGameObject = targetRenderer.gameObject;
                }
            }
        }
        #endregion

        #region Chargement
        private static Renderer FindRenderer(GameObject go)
        {
            if (go == null)
                return null;

            foreach (Renderer candidate in go.GetComponentsInChildren<Renderer>(true))
            {
                if (VertexClothEditorUtility.IsSupported(candidate))
                    return candidate;
            }

            return null;
        }

        private void Load()
        {
            DestroyPreview();
            topology = null;
            history.Clear();

            Mesh mesh = SourceMesh;
            if (targetRenderer == null || mesh == null || !mesh.isReadable)
                return;

            topology = VertexClothTopologyBuilder.Build(mesh);
            meshVertices = mesh.vertices;
            freedom = new float[topology.ParticleCount];

            VertexClothSimulator simulator = targetRenderer.GetComponent<VertexClothSimulator>();
            if (dataAsset == null && simulator != null)
            {
                dataAsset = simulator.Data;
                if (settingsAsset == null)
                    settingsAsset = simulator.Settings;
            }

            if (dataAsset != null && dataAsset.IsValidFor(mesh) && dataAsset.ParticleCount == topology.ParticleCount)
                System.Array.Copy(dataAsset.particleFreedom, freedom, freedom.Length);

            AutoHeights();
            BuildPreview();
            Repaint();
        }

        private void AutoHeights()
        {
            Mesh mesh = SourceMesh;
            if (mesh == null)
                return;

            Bounds b = mesh.bounds;
            pinHeight = b.min.y + b.size.y * 0.65f;
            freeHeight = b.min.y + b.size.y * 0.2f;
        }
        #endregion

        #region Édition des poids
        private void PushHistory()
        {
            history.Add((float[])freedom.Clone());
            if (history.Count > MaxHistory)
                history.RemoveAt(0);
        }

        private void UndoLast()
        {
            if (history.Count == 0)
                return;

            freedom = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            RefreshPreviewColors();
            SceneView.RepaintAll();
        }

        private void FillAll(float value)
        {
            PushHistory();
            for (int i = 0; i < freedom.Length; i++)
                freedom[i] = value;
            RefreshPreviewColors();
            SceneView.RepaintAll();
        }

        private void InvertAll()
        {
            PushHistory();
            for (int i = 0; i < freedom.Length; i++)
                freedom[i] = 1f - freedom[i];
            RefreshPreviewColors();
            SceneView.RepaintAll();
        }

        private void ApplyPreset()
        {
            if (preset == null)
                return;

            PushHistory();
            freedom = VertexClothAutoWeights.Compute(topology, SourceMesh, preset);

            if (settingsAsset == null)
                settingsAsset = preset.settings;

            RefreshPreviewColors();
            SceneView.RepaintAll();
        }

        private void ApplyHeightGradient()
        {
            PushHistory();
            for (int i = 0; i < freedom.Length; i++)
            {
                float f = Mathf.InverseLerp(pinHeight, freeHeight, topology.ParticlePositions[i].y);
                freedom[i] = f * f * (3f - 2f * f);
            }

            RefreshPreviewColors();
            SceneView.RepaintAll();
        }

        private void ApplyBrush()
        {
            float localRadius = brushRadius / GetAverageScale(targetRenderer.transform);

            ApplyAt(hoverLocalPoint, hoverLocalNormal, localRadius);
            if (mirrorX)
            {
                ApplyAt(new Vector3(-hoverLocalPoint.x, hoverLocalPoint.y, hoverLocalPoint.z),
                        new Vector3(-hoverLocalNormal.x, hoverLocalNormal.y, hoverLocalNormal.z),
                        localRadius);
            }

            RefreshPreviewColors();
        }

        private void ApplyAt(Vector3 center, Vector3 normal, float radius)
        {
            Vector3[] positions = topology.ParticlePositions;

            if (mode == BrushMode.Island)
            {
                int nearest = FindNearestParticle(center);
                if (nearest < 0)
                    return;

                int island = topology.IslandOfParticle[nearest];
                for (int i = 0; i < freedom.Length; i++)
                {
                    if (topology.IslandOfParticle[i] == island)
                        freedom[i] = paintValue;
                }

                return;
            }

            float average = 0f;
            if (mode == BrushMode.Smooth)
            {
                float sum = 0f;
                float weightSum = 0f;
                for (int i = 0; i < positions.Length; i++)
                {
                    float f = Falloff(Vector3.Distance(positions[i], center), radius);
                    if (f <= 0f) continue;
                    sum += freedom[i] * f;
                    weightSum += f;
                }

                if (weightSum <= 0f)
                    return;
                average = sum / weightSum;
            }

            for (int i = 0; i < positions.Length; i++)
            {
                float f = Falloff(Vector3.Distance(positions[i], center), radius);
                if (f <= 0f)
                    continue;

                if (facingOnly && Vector3.Dot(topology.ParticleNormals[i], normal) <= 0f)
                    continue;

                float target = mode == BrushMode.Smooth ? average : paintValue;
                freedom[i] = Mathf.Lerp(freedom[i], target, f * brushStrength);
            }
        }

        private float Falloff(float distance, float radius)
        {
            float t = distance / radius;
            if (t >= 1f) return 0f;
            if (t <= brushHardness) return 1f;
            return 1f - (t - brushHardness) / Mathf.Max(1e-4f, 1f - brushHardness);
        }

        private int FindNearestParticle(Vector3 point)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            Vector3[] positions = topology.ParticlePositions;

            for (int i = 0; i < positions.Length; i++)
            {
                float sqr = (positions[i] - point).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
        }

        private static float GetAverageScale(Transform t)
        {
            Vector3 s = t.lossyScale;
            return Mathf.Max(1e-5f, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f);
        }
        #endregion

        #region Scene view
        private void OnSceneGUI(SceneView sceneView)
        {
            if (targetRenderer == null || topology == null || previewMesh == null)
                return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Repaint)
                DrawOverlay();

            if (!paintEnabled || e.alt)
                return;

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.MouseMove:
                    UpdateHover(e.mousePosition);
                    sceneView.Repaint();
                    break;

                case EventType.MouseDown:
                    if (e.button != 0) break;
                    UpdateHover(e.mousePosition);
                    if (!hasHover) break;
                    GUIUtility.hotControl = controlId;
                    PushHistory();
                    ApplyBrush();
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId) break;
                    UpdateHover(e.mousePosition);
                    if (hasHover) ApplyBrush();
                    e.Use();
                    sceneView.Repaint();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId) break;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;

                case EventType.Repaint:
                    DrawBrush();
                    break;
            }
        }

        private void UpdateHover(Vector2 mousePosition)
        {
            Transform tr = targetRenderer.transform;
            Ray worldRay = HandleUtility.GUIPointToWorldRay(mousePosition);
            var localRay = new Ray(
                tr.InverseTransformPoint(worldRay.origin),
                tr.InverseTransformDirection(worldRay.direction));

            hasHover = VertexClothRaycast.Raycast(meshVertices, topology.MeshTriangles, localRay,
                out float distance, out int triangle);

            if (!hasHover)
                return;

            hoverTriangle = triangle;
            hoverLocalPoint = localRay.GetPoint(distance);

            Vector3 v0 = meshVertices[topology.MeshTriangles[triangle * 3]];
            Vector3 v1 = meshVertices[topology.MeshTriangles[triangle * 3 + 1]];
            Vector3 v2 = meshVertices[topology.MeshTriangles[triangle * 3 + 2]];
            Vector3 n = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            if (Vector3.Dot(n, localRay.direction) > 0f)
                n = -n;
            hoverLocalNormal = n;
        }

        private void DrawBrush()
        {
            if (!paintEnabled || !hasHover)
                return;

            Transform tr = targetRenderer.transform;
            Handles.color = Color.white;
            Handles.DrawWireDisc(tr.TransformPoint(hoverLocalPoint), tr.TransformDirection(hoverLocalNormal), brushRadius);

            if (mirrorX)
            {
                Vector3 mirroredPoint = new Vector3(-hoverLocalPoint.x, hoverLocalPoint.y, hoverLocalPoint.z);
                Vector3 mirroredNormal = new Vector3(-hoverLocalNormal.x, hoverLocalNormal.y, hoverLocalNormal.z);
                Handles.color = new Color(1f, 1f, 1f, 0.35f);
                Handles.DrawWireDisc(tr.TransformPoint(mirroredPoint), tr.TransformDirection(mirroredNormal), brushRadius);
            }
        }
        #endregion

        #region Aperçu
        private void BuildPreview()
        {
            Mesh mesh = SourceMesh;
            float offset = mesh.bounds.size.magnitude * PreviewOffsetFactor;

            var vertices = new Vector3[meshVertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = meshVertices[i] + topology.ParticleNormals[topology.VertexToParticle[i]] * offset;

            previewMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            previewMesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            previewMesh.vertices = vertices;
            previewMesh.triangles = topology.MeshTriangles;

            previewColors = new Color[vertices.Length];
            RefreshPreviewColors();
        }

        private void RefreshPreviewColors()
        {
            if (previewMesh == null || previewColors == null)
                return;

            for (int i = 0; i < previewColors.Length; i++)
            {
                Color c = WeightToColor(freedom[topology.VertexToParticle[i]]);
                c.a = overlayOpacity;
                previewColors[i] = c;
            }

            previewMesh.colors = previewColors;
        }

        private static Color WeightToColor(float w)
        {
            var blue = new Color(0.1f, 0.3f, 1f);
            var yellow = new Color(1f, 0.9f, 0.1f);
            var red = new Color(1f, 0.15f, 0.1f);
            return w < 0.5f ? Color.Lerp(blue, yellow, w * 2f) : Color.Lerp(yellow, red, (w - 0.5f) * 2f);
        }

        private void DrawOverlay()
        {
            if (overlayMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                overlayMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                SetMaterialInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                SetMaterialInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                SetMaterialInt("_Cull", (int)CullMode.Off);
                SetMaterialInt("_ZWrite", 0);
                SetMaterialInt("_ZTest", (int)CompareFunction.LessEqual);
            }

            overlayMaterial.SetPass(0);
            Graphics.DrawMeshNow(previewMesh, targetRenderer.transform.localToWorldMatrix);
        }

        private void SetMaterialInt(string property, int value)
        {
            if (overlayMaterial.HasProperty(property))
                overlayMaterial.SetInt(property, value);
        }

        private void DestroyPreview()
        {
            if (previewMesh != null)
                DestroyImmediate(previewMesh);
            previewMesh = null;
            previewColors = null;
        }
        #endregion

        #region Bake
        private bool Bake()
        {
            if (topology == null || targetRenderer == null)
                return false;

            Mesh mesh = SourceMesh;
            string newPath = null;

            if (dataAsset == null)
            {
                newPath = EditorUtility.SaveFilePanelInProject(
                    "Sauvegarder les données cloth", mesh.name + "_ClothData", "asset", "Choisis l'emplacement de l'asset.");
                if (string.IsNullOrEmpty(newPath))
                    return false;
            }

            dataAsset = VertexClothEditorUtility.Bake(mesh, topology, freedom, dataAsset, newPath);

            VertexClothSettings fallback = settingsAsset != null ? settingsAsset : (preset != null ? preset.settings : null);
            settingsAsset = VertexClothEditorUtility.EnsureSettings(fallback, VertexClothEditorUtility.FolderOf(dataAsset));

            AssetDatabase.SaveAssets();
            return true;
        }
        #endregion
    }
}
