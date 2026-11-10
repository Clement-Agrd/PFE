using UnityEditor;
using UnityEngine;

namespace Core.GroundFog.EditorTools
{
    /// <summary>Menu GameObject > Effects > Ground Fog Zone : crée une zone prête à l'emploi.</summary>
    public static class GroundFogMenu
    {
        private const string ShaderName = "Custom/GroundFog";
        private const string PresetPath = "Assets/ScriptableObjects/GroundFogPreset_Cour.asset";
        private const string MaterialPath = "Assets/Materials/GroundFog.mat";

        [MenuItem("GameObject/Effects/Ground Fog Zone", false, 10)]
        public static void CreateZone(MenuCommand command)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "GroundFogZone";
            Object.DestroyImmediate(go.GetComponent<BoxCollider>()); // pas de collision

            // Taille par défaut : 16 x 1.5 x 16, posé sur le sol (centre = demi-hauteur).
            go.transform.localScale = new Vector3(16f, 1.5f, 16f);
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            go.transform.position = SceneViewPivot() + Vector3.up * 0.75f;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = GetOrCreateMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var zone = go.AddComponent<GroundFogZone>();
            zone.SetPreset(GetOrCreatePreset());

            Undo.RegisterCreatedObjectUndo(go, "Create Ground Fog Zone");
            Selection.activeGameObject = go;
        }

        private static Vector3 SceneViewPivot()
        {
            SceneView view = SceneView.lastActiveSceneView;
            return view != null ? view.pivot : Vector3.zero;
        }

        private static Material GetOrCreateMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;
            mat = new Material(Shader.Find(ShaderName));
            AssetDatabase.CreateAsset(mat, MaterialPath);
            return mat;
        }

        private static GroundFogPreset GetOrCreatePreset()
        {
            var preset = AssetDatabase.LoadAssetAtPath<GroundFogPreset>(PresetPath);
            if (preset != null) return preset;
            preset = ScriptableObject.CreateInstance<GroundFogPreset>();
            AssetDatabase.CreateAsset(preset, PresetPath);
            return preset;
        }
    }
}
