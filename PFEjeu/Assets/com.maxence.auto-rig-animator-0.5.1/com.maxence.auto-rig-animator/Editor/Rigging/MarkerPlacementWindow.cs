using UnityEditor;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>
    /// Phase 1 of the Auto Rig pipeline: click landmarks (Hips, Head, elbows, knees...) directly on a
    /// raw mesh in the Scene view. The resulting <see cref="MarkerSet"/> is what Phase 2 (auto-skinning)
    /// and the skeleton builder will consume. This window only places and edits markers; it does not
    /// create any bone yet.
    /// </summary>
    internal sealed class MarkerPlacementWindow : EditorWindow
    {
        #region Constants

        private const string WindowTitle = "Auto Rig – Landmarks";
        private const float HandleSize = 0.035f;
        private const float LabelVerticalOffset = 0.05f;
        private static readonly Color PlacedMarkerColor = new Color(0.2f, 0.85f, 0.3f);
        private static readonly Color CurrentMarkerColor = new Color(1f, 0.75f, 0.1f);
        private static readonly Color OptionalMarkerColor = new Color(0.4f, 0.65f, 1f);

        #endregion

        #region Serialized State

        [SerializeField] private GameObject _targetObject;
        [SerializeField] private MarkerSet _markers = new MarkerSet();
        [SerializeField] private string _currentBoneName;

        #endregion

        #region Runtime State

        // Not serialized: re-resolved every time the target changes or the window re-enables.
        private MeshCollider _tempCollider;
        private bool _weAddedCollider;

        #endregion

        [MenuItem("Tools/Auto Rig/Place Landmarks")]
        public static void Open()
        {
            GetWindow<MarkerPlacementWindow>(WindowTitle);
        }

        #region Unity Lifecycle

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            SceneView.duringSceneGui += OnSceneGUI;

            if (_targetObject != null)
            {
                SetupTempCollider();
            }
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            CleanupTempCollider();
        }

        #endregion

        #region Window GUI

        private void OnGUI()
        {
            DrawTargetField();

            if (_targetObject == null)
            {
                EditorGUILayout.HelpBox("Glisse ici le mesh brut à rigger (doit avoir un MeshFilter).", MessageType.Info);
                return;
            }

            DrawColliderWarningIfNeeded();

            EditorGUILayout.Space();
            DrawProgress();
            EditorGUILayout.Space();
            DrawBoneList();
            EditorGUILayout.Space();
            DrawFooterButtons();
            DrawAvatarSection();
        }

        private void DrawTargetField()
        {
            EditorGUI.BeginChangeCheck();
            var newTarget = (GameObject)EditorGUILayout.ObjectField("Mesh brut", _targetObject, typeof(GameObject), true);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            if (newTarget != null && !TryValidateNewTarget(newTarget, out string rejectionReason))
            {
                EditorUtility.DisplayDialog(WindowTitle, rejectionReason, "OK");
                return;
            }

            CleanupTempCollider();
            _targetObject = newTarget;
            _markers.Clear();
            _currentBoneName = null;

            if (_targetObject != null)
            {
                SetupTempCollider();
                _currentBoneName = _markers.GetNextUnplacedRequiredBone();
            }
        }

        /// <summary>
        /// Rejects a target this tool cannot safely work on, with a clear reason instead of a
        /// confusing downstream failure — a raycast that silently never hits anything, or (the
        /// serious one) a Prefab ASSET dragged straight from the Project window: adding a temporary
        /// MeshCollider to it would modify the shared asset on disk, affecting every instance of that
        /// Prefab in the project, not just a scene copy. [Likely] the right check for that case: a
        /// Prefab asset has no valid scene; a scene instance of it (or one being edited in Prefab Mode)
        /// does, so both of those are still allowed through.
        /// </summary>
        private static bool TryValidateNewTarget(GameObject target, out string rejectionReason)
        {
            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                rejectionReason = "Cet objet n'a pas de MeshFilter : impossible de raycaster dessus.";
                return false;
            }

            if (meshFilter.sharedMesh == null || meshFilter.sharedMesh.vertexCount == 0)
            {
                rejectionReason = "Le mesh est vide (0 sommet) : il n'y a rien à rigger dessus.";
                return false;
            }

            if (!target.scene.IsValid())
            {
                rejectionReason = "Glisse une INSTANCE du mesh placée dans une scène, pas l'asset Prefab du Project window : " +
                    "modifier l'asset directement affecterait toutes ses instances dans le projet.";
                return false;
            }

            rejectionReason = null;
            return true;
        }

        /// <summary>
        /// Warns and offers a one-click fix if the temporary collider this window relies on for
        /// raycasting was destroyed by something else since it was set up (another tool, a script, a
        /// manual delete in the Hierarchy) — without this, clicks in the Scene view would silently do
        /// nothing with no visible explanation.
        /// </summary>
        private void DrawColliderWarningIfNeeded()
        {
            if (_tempCollider != null)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "Le collider temporaire utilisé pour viser le mesh a disparu (supprimé par autre chose). " +
                "Les clics dans la Scene view ne placeront aucun repère tant qu'il n'est pas recréé.",
                MessageType.Warning);

            if (GUILayout.Button("Recréer le collider"))
            {
                SetupTempCollider();
            }
        }

        private void DrawProgress()
        {
            int requiredCount = 0;
            for (int i = 0; i < BoneTemplate.Bones.Length; i++)
            {
                if (!BoneTemplate.Bones[i].IsOptional)
                {
                    requiredCount++;
                }
            }

            int placedRequired = requiredCount - CountUnplacedRequired();
            EditorGUILayout.LabelField($"Repères obligatoires : {placedRequired} / {requiredCount}");

            if (!string.IsNullOrEmpty(_currentBoneName))
            {
                EditorGUILayout.HelpBox($"Clique sur le mesh dans la Scene view pour placer : {_currentBoneName}", MessageType.None);
            }
        }

        private int CountUnplacedRequired()
        {
            int count = 0;
            for (int i = 0; i < BoneTemplate.Bones.Length; i++)
            {
                BoneDefinition bone = BoneTemplate.Bones[i];
                if (!bone.IsOptional && !_markers.TryGetPosition(bone.Name, out _))
                {
                    count++;
                }
            }

            return count;
        }

        private void DrawBoneList()
        {
            for (int i = 0; i < BoneTemplate.Bones.Length; i++)
            {
                BoneDefinition bone = BoneTemplate.Bones[i];
                bool isPlaced = _markers.TryGetPosition(bone.Name, out _);
                bool isCurrent = bone.Name == _currentBoneName;

                using (new EditorGUILayout.HorizontalScope())
                {
                    string status = isPlaced ? "✓" : (bone.IsOptional ? "–" : "○");
                    EditorGUILayout.LabelField(status, GUILayout.Width(16f));

                    GUIStyle labelStyle = isCurrent ? EditorStyles.boldLabel : EditorStyles.label;
                    string suffix = bone.IsOptional ? " (optionnel)" : string.Empty;
                    EditorGUILayout.LabelField(bone.Name + suffix, labelStyle);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Placer ici", EditorStyles.miniButton, GUILayout.Width(80f)))
                    {
                        _currentBoneName = bone.Name;
                    }

                    using (new EditorGUI.DisabledScope(!isPlaced))
                    {
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22f)))
                        {
                            _markers.Remove(bone.Name);
                            if (string.IsNullOrEmpty(_currentBoneName))
                            {
                                _currentBoneName = bone.Name;
                            }
                        }
                    }
                }
            }
        }

        private void DrawFooterButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Tout effacer"))
                {
                    if (EditorUtility.DisplayDialog(WindowTitle, "Effacer tous les repères placés ?", "Oui", "Annuler"))
                    {
                        _markers.Clear();
                        _currentBoneName = _markers.GetNextUnplacedRequiredBone();
                    }
                }

                using (new EditorGUI.DisabledScope(!_markers.HasAllRequiredMarkers()))
                {
                    if (GUILayout.Button("Construire le squelette"))
                    {
                        BuildSkeleton();
                    }
                }
            }
        }

        private void BuildSkeleton()
        {
            AutoRigPipeline.Result result = AutoRigPipeline.BuildSkeletonAndSkin(_targetObject, _markers);

            switch (result)
            {
                case AutoRigPipeline.Result.Success:
                    EditorUtility.DisplayDialog(WindowTitle, "Squelette construit et skinné. Ctrl+Z annule toute l'opération en un coup.", "OK");
                    break;
                case AutoRigPipeline.Result.AlreadyRigged:
                    EditorUtility.DisplayDialog(WindowTitle, "Cet objet a déjà un SkinnedMeshRenderer. Annule (Ctrl+Z) la construction précédente avant d'en refaire une.", "OK");
                    break;
                case AutoRigPipeline.Result.MissingMeshFilter:
                    EditorUtility.DisplayDialog(WindowTitle, "Mesh introuvable (MeshFilter manquant ou vide).", "OK");
                    break;
                case AutoRigPipeline.Result.EmptyMesh:
                    EditorUtility.DisplayDialog(WindowTitle, "Le mesh n'a aucun sommet : rien à skinner.", "OK");
                    break;
                case AutoRigPipeline.Result.InvalidInput:
                    EditorUtility.DisplayDialog(WindowTitle, "Objet ou repères invalides. Vérifie que tous les repères obligatoires sont placés et réessaie.", "OK");
                    break;
                case AutoRigPipeline.Result.Cancelled:
                    EditorUtility.DisplayDialog(WindowTitle, "Construction annulée : aucun objet partiel n'a été laissé derrière.", "OK");
                    break;
                case AutoRigPipeline.Result.UnexpectedError:
                    EditorUtility.DisplayDialog(WindowTitle, "Erreur inattendue pendant la construction. L'opération a été annulée proprement (rien de partiel ne reste) — regarde la Console pour le détail.", "OK");
                    break;
            }
        }

        private void DrawAvatarSection()
        {
            if (_targetObject.GetComponent<SkinnedMeshRenderer>() == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Avatar Humanoid", EditorStyles.boldLabel);

            if (GUILayout.Button("Générer l'Avatar Humanoid"))
            {
                BuildHumanoidAvatar();
            }
        }

        private void BuildHumanoidAvatar()
        {
            AutoRigPipeline.AvatarResult result = AutoRigPipeline.BuildHumanoidAvatar(_targetObject, out Avatar avatar, out GameObject rigRoot);

            switch (result)
            {
                case AutoRigPipeline.AvatarResult.Success:
                    EditorUtility.DisplayDialog(WindowTitle, $"Avatar Humanoid généré et sauvegardé dans Assets/AutoRig/Avatars/.\nRacine : {rigRoot.name}", "OK");
                    Selection.activeGameObject = rigRoot;
                    break;
                case AutoRigPipeline.AvatarResult.NotRiggedYet:
                    EditorUtility.DisplayDialog(WindowTitle, "Construis d'abord le squelette (bouton ci-dessus).", "OK");
                    break;
                case AutoRigPipeline.AvatarResult.InvalidAvatar:
                    EditorUtility.DisplayDialog(WindowTitle, "Avatar invalide : vérifie que le mesh était bien à peu près en T-pose au moment du placement des repères.", "OK");
                    break;
                case AutoRigPipeline.AvatarResult.BuildThrew:
                    EditorUtility.DisplayDialog(WindowTitle, "Erreur pendant la construction de l'Avatar. L'opération a été annulée proprement (rien de partiel ne reste) — regarde la Console pour le détail.", "OK");
                    break;
                case AutoRigPipeline.AvatarResult.InvalidInput:
                    EditorUtility.DisplayDialog(WindowTitle, "Objet invalide.", "OK");
                    break;
                case AutoRigPipeline.AvatarResult.UnexpectedError:
                    EditorUtility.DisplayDialog(WindowTitle, "Erreur inattendue pendant la construction de l'Avatar. L'opération a été annulée proprement — regarde la Console pour le détail.", "OK");
                    break;
            }
        }

        #endregion

        #region Temp Collider

        private void SetupTempCollider()
        {
            _tempCollider = _targetObject.GetComponent<MeshCollider>();
            _weAddedCollider = _tempCollider == null;

            if (_weAddedCollider)
            {
                _tempCollider = _targetObject.AddComponent<MeshCollider>();
            }

            var meshFilter = _targetObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                _tempCollider.sharedMesh = meshFilter.sharedMesh;
            }
        }

        private void CleanupTempCollider()
        {
            // Only remove the collider if WE added it: an existing one on the user's object is left alone.
            if (_weAddedCollider && _tempCollider != null)
            {
                DestroyImmediate(_tempCollider);
            }

            _tempCollider = null;
            _weAddedCollider = false;
        }

        #endregion

        #region Scene View

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_targetObject == null || _tempCollider == null)
            {
                return;
            }

            DrawPlacedMarkerHandles();

            if (string.IsNullOrEmpty(_currentBoneName))
            {
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);

            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                if (TryRaycastMesh(evt.mousePosition, out Vector3 worldPoint))
                {
                    Undo.RecordObject(this, "Place Landmark");
                    _markers.Set(_currentBoneName, worldPoint);
                    _currentBoneName = _markers.GetNextUnplacedRequiredBone();
                    Repaint();
                }

                evt.Use();
            }

            sceneView.Repaint();
        }

        /// <summary>
        /// Casts through the mesh both ways and places the landmark at the midpoint of the front and
        /// back surface hits, instead of right on the clicked (front) skin. For a thin part (a wrist)
        /// this barely moves the point; for a wide, round part (hips, head) it matters a lot — a
        /// surface-only point sits skewed toward the camera and skews the skeleton and the skinning.
        /// </summary>
        private bool TryRaycastMesh(Vector2 mousePosition, out Vector3 worldPoint)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            if (!TryRaycastCollider(ray, float.MaxValue, out Vector3 frontPoint))
            {
                worldPoint = default;
                return false;
            }

            Vector3 direction = ray.direction.normalized;
            float probeDistance = _tempCollider.bounds.size.magnitude * 2f;
            var backwardRay = new Ray(frontPoint + direction * probeDistance, -direction);

            worldPoint = TryRaycastCollider(backwardRay, probeDistance, out Vector3 backPoint)
                ? Vector3.Lerp(frontPoint, backPoint, 0.5f)
                : frontPoint;

            return true;
        }

        /// <summary>
        /// Nearest hit on <see cref="_tempCollider"/> specifically, picked explicitly by distance —
        /// Physics.RaycastAll does not guarantee hit order, so scanning for the first match in the
        /// array (the previous approach) could silently return a farther point on a non-convex mesh.
        /// </summary>
        private bool TryRaycastCollider(Ray ray, float maxDistance, out Vector3 point)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance);
            float bestDistance = float.MaxValue;
            point = default;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == _tempCollider && hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    point = hits[i].point;
                    found = true;
                }
            }

            return found;
        }

        private void DrawPlacedMarkerHandles()
        {
            for (int i = 0; i < BoneTemplate.Bones.Length; i++)
            {
                BoneDefinition bone = BoneTemplate.Bones[i];
                if (!_markers.TryGetPosition(bone.Name, out Vector3 position))
                {
                    continue;
                }

                Color color = bone.Name == _currentBoneName
                    ? CurrentMarkerColor
                    : (bone.IsOptional ? OptionalMarkerColor : PlacedMarkerColor);

                using (new Handles.DrawingScope(color))
                {
                    float size = HandleUtility.GetHandleSize(position) * HandleSize;
                    Vector3 moved = Handles.FreeMoveHandle(position, size, Vector3.zero, Handles.SphereHandleCap);
                    if (moved != position)
                    {
                        Undo.RecordObject(this, "Move Landmark");
                        _markers.Set(bone.Name, moved);
                    }

                    Handles.Label(position + Vector3.up * LabelVerticalOffset, bone.Name);
                }
            }
        }

        #endregion
    }
}
