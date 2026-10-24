using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DustVFX.EditorTools
{
    /// <summary>Creates the DustSurface assets, their PhysicsMaterials and the Resources/DustSettings asset.</summary>
    public static class DustSurfaceBuilder
    {
        public const string Root = "Assets/DustVFX";
        public const string SurfacesDir = Root + "/Surfaces";
        public const string SettingsPath = Root + "/Resources/DustSettings.asset";

        public static readonly string[] SurfaceNames = { "Earth", "Sand", "Stone", "Snow", "Grass", "Wood", "None" };

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        public static PhysicsMaterial LoadOrCreatePhysics(string name, float friction, float bounce)
        {
            string path = $"{SurfacesDir}/Phys_{name}.asset";
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m != null) return m;
            m = new PhysicsMaterial(name) { dynamicFriction = friction, staticFriction = friction, bounciness = bounce };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void EnsureTag(string tag)
        {
            foreach (string t in InternalEditorUtility.tags)
                if (t == tag) return;
            InternalEditorUtility.AddTag(tag);
        }

        public static DustSettings BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(SurfacesDir)) AssetDatabase.CreateFolder(Root, "Surfaces");
            if (!AssetDatabase.IsValidFolder(Root + "/Resources")) AssetDatabase.CreateFolder(Root, "Resources");

            EnsureTag("DustGrass");
            EnsureTag("DustWood");

            var earth = Surface("Earth", DustSurfaceKind.Earth, s =>
            {
                s.color = new Color(0.50f, 0.37f, 0.26f, 1f);
                s.densityMul = 1.0f; s.sizeMul = 1.0f; s.lifetimeMul = 1.0f; s.riseMul = 1.0f; s.veilMul = 1.0f;
                s.grainCount = 10; s.grainSize = new Vector2(0.025f, 0.055f);
                s.grainColor = new Color(0.36f, 0.26f, 0.18f, 1f);
                s.grainBounce = 0.3f; s.grainSpeedMul = 1f;
                s.physicsMaterials = new[] { LoadOrCreatePhysics("Earth", 0.7f, 0.05f) };
            });
            var sand = Surface("Sand", DustSurfaceKind.Sand, s =>
            {
                s.color = new Color(0.88f, 0.76f, 0.52f, 1f);
                s.densityMul = 1.0f; s.sizeMul = 1.15f; s.lifetimeMul = 1.15f; s.riseMul = 1.6f; s.veilMul = 0.9f;
                s.grainCount = 15; s.grainSize = new Vector2(0.012f, 0.03f);
                s.grainColor = new Color(0.82f, 0.68f, 0.44f, 1f);
                s.grainBounce = 0.15f; s.grainSpeedMul = 1.1f;
                s.physicsMaterials = new[] { LoadOrCreatePhysics("Sand", 0.8f, 0.0f) };
            });
            var stone = Surface("Stone", DustSurfaceKind.Stone, s =>
            {
                s.color = new Color(0.62f, 0.64f, 0.68f, 1f);
                s.densityMul = 0.45f; s.sizeMul = 0.7f; s.lifetimeMul = 0.8f; s.riseMul = 0.6f; s.veilMul = 0.45f;
                s.grainCount = 9; s.grainSize = new Vector2(0.02f, 0.05f);
                s.grainColor = new Color(0.5f, 0.5f, 0.53f, 1f);
                s.grainBounce = 0.5f; s.grainSpeedMul = 1.5f;
                s.physicsMaterials = new[] { LoadOrCreatePhysics("Stone", 0.6f, 0.1f) };
            });
            var snow = Surface("Snow", DustSurfaceKind.Snow, s =>
            {
                s.color = new Color(0.90f, 0.95f, 1.0f, 1f);
                s.densityMul = 0.9f; s.sizeMul = 1.1f; s.lifetimeMul = 1.55f; s.riseMul = 1.3f; s.veilMul = 1.1f;
                s.grainCount = 12; s.grainSize = new Vector2(0.012f, 0.03f);
                s.grainColor = new Color(0.92f, 0.96f, 1.0f, 1f);
                s.grainBounce = 0.05f; s.grainSpeedMul = 0.6f;
                s.physicsMaterials = new[] { LoadOrCreatePhysics("Snow", 0.4f, 0.0f) };
            });
            var grass = Surface("Grass", DustSurfaceKind.Grass, s =>
            {
                s.color = new Color(0.50f, 0.52f, 0.32f, 1f);
                s.densityMul = 0.22f; s.sizeMul = 0.6f; s.lifetimeMul = 0.7f; s.riseMul = 0.5f; s.veilMul = 0.25f;
                s.grainCount = 5; s.grainSize = new Vector2(0.02f, 0.045f);
                s.grainColor = new Color(0.30f, 0.45f, 0.16f, 1f);
                s.grainBounce = 0.1f; s.grainSpeedMul = 0.8f;
                s.tags = new[] { "DustGrass" };
            });
            var wood = Surface("Wood", DustSurfaceKind.Wood, s =>
            {
                s.color = new Color(0.66f, 0.54f, 0.40f, 1f);
                s.densityMul = 0.12f; s.sizeMul = 0.5f; s.lifetimeMul = 0.6f; s.riseMul = 0.4f; s.veilMul = 0.1f;
                s.grainCount = 3; s.grainSize = new Vector2(0.015f, 0.035f);
                s.grainColor = new Color(0.5f, 0.38f, 0.26f, 1f);
                s.grainBounce = 0.2f; s.grainSpeedMul = 0.8f;
                s.tags = new[] { "DustWood" };
            });
            var none = Surface("None", DustSurfaceKind.None, s =>
            {
                s.suppressDust = true;
                s.densityMul = 0f;
                s.grainCount = 0;
            });

            var settings = LoadOrCreate<DustSettings>(SettingsPath);
            settings.surfaces = new[] { earth, sand, stone, snow, grass, wood, none };
            settings.defaultSurface = earth;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        static DustSurface Surface(string name, DustSurfaceKind kind, System.Action<DustSurface> init)
        {
            var s = LoadOrCreate<DustSurface>($"{SurfacesDir}/Surface_{name}.asset");
            s.kind = kind;
            init(s);
            EditorUtility.SetDirty(s);
            return s;
        }
    }
}
