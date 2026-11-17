#if UNITY_EDITOR
using Core.DayNightSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Core.FXZone.Editor
{
    /// <summary>
    /// Pose une GroundFogZone qui recouvre le sol de ZONE_07_FX (Sol_FX), avec le material, le preset
    /// et le DayNightCycle déjà présents dans le projet. Ne modifie aucun asset existant.
    ///
    /// ZONE_07_FX
    /// ├── Sol_FX
    /// └── GroundFogZone   (cube sans collider, Custom/GroundFog, suit DayNightCycle)
    /// </summary>
    public static class FXZoneGroundFogPlacer
    {
        #region Constantes
        private const string ZoneName = "ZONE_07_FX";
        private const string FloorName = "Sol_FX";
        private const string FogName = "GroundFogZone";
        private const string PresetPath = "Assets/ScriptableObjects/GroundFogPreset_Cour.asset";
        private const string MaterialPath = "Assets/Materials/GroundFog.mat";

        private const float FogHeight = 1.5f;
        private static readonly Vector3 FallbackSize = new Vector3(24f, FogHeight, 24f);
        #endregion

        [MenuItem("Tools/FX Zone/Placer le ground fog")]
        public static void Place()
        {
            GameObject zone = GameObject.Find(ZoneName);
            if (zone == null)
            {
                Debug.LogError($"[FXZone] {ZoneName} introuvable dans la scène ouverte (ouvre SceneTrainingRoom).");
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var preset = AssetDatabase.LoadAssetAtPath<Core.GroundFog.GroundFogPreset>(PresetPath);
            if (material == null || preset == null)
            {
                Debug.LogError("[FXZone] Material GroundFog.mat ou preset GroundFogPreset_Cour introuvable (voir GameObject > Effects > Ground Fog Zone).");
                return;
            }

            // Idempotent : on remplace uniquement la zone créée par cet outil.
            Transform existing = zone.transform.Find(FogName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            ComputeArea(zone.transform, out Vector3 center, out Vector3 size);

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = FogName;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>()); // pas de collision

            go.transform.position = center;
            go.transform.localScale = size;
            go.transform.SetParent(zone.transform, true);

            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var fog = go.AddComponent<Core.GroundFog.GroundFogZone>();
            fog.SetPreset(preset);

            // dayNight est privé : on l'assigne par SerializedObject pour que le brouillard suive l'heure choisie dans la console FX.
            var cycle = Object.FindFirstObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                var so = new SerializedObject(fog);
                so.FindProperty("dayNight").objectReferenceValue = cycle;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[FXZone] Aucun DayNightCycle dans la scène : le brouillard utilise la valeur manuelle (nuit).");
            }

            Undo.RegisterCreatedObjectUndo(go, "Place Ground Fog");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            Debug.Log($"[FXZone] GroundFogZone posée sous {ZoneName} ({size.x:0.#} x {size.y:0.#} x {size.z:0.#} m).");
        }

        #region Calcul de la zone
        // Recouvre le sol Sol_FX s'il existe, sinon une zone par défaut centrée sur ZONE_07_FX.
        private static void ComputeArea(Transform zone, out Vector3 center, out Vector3 size)
        {
            Transform floor = FindDeep(zone, FloorName);
            if (floor != null)
            {
                var renderers = floor.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                    size = new Vector3(bounds.size.x, FogHeight, bounds.size.z);
                    center = new Vector3(bounds.center.x, bounds.max.y + FogHeight * 0.5f, bounds.center.z);
                    return;
                }
            }

            size = FallbackSize;
            center = zone.position + Vector3.up * (FogHeight * 0.5f);
            Debug.LogWarning("[FXZone] Sol_FX introuvable : zone de brouillard par défaut, à ajuster à la main.");
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            foreach (Transform child in root)
            {
                if (child.name == childName) return child;
                Transform found = FindDeep(child, childName);
                if (found != null) return found;
            }
            return null;
        }
        #endregion
    }
}
#endif
