using UnityEditor;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>
    /// Onglet « Anti-répétition » : active le shader PFE/TerrainAntiTile sur le terrain, règle les paramètres globaux
    /// (matériau) et la force par couche (TerrainLayer.maskMapRemapMax.z = 1 - force). Undo partout, réversible.
    /// </summary>
    public static class AntiTilePanel
    {
        public const string MaterialPath = "Assets/Shaders/TerrainAntiTile/Terrain_AntiTile.mat";
        private const string ShaderName = "PFE/TerrainAntiTile";
        private const string StandardMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat";
        private const string KeywordPerPixelNormal = "_TERRAIN_INSTANCED_PERPIXEL_NORMAL";

        private struct Param
        {
            public string label, property;
            public float min, max;
            public Param(string label, string property, float min, float max) { this.label = label; this.property = property; this.min = min; this.max = max; }
        }

        private static readonly Param[] StochasticParams =
        {
            new Param("Fondu stochastique : début (m)", "_AT_FadeStart", 0f, 500f),
            new Param("Fondu stochastique : fin (m)", "_AT_FadeEnd", 1f, 800f),
            new Param("Restauration du contraste", "_AT_HexContrast", 0f, 1f),
        };
        private static readonly Param[] FarParams =
        {
            new Param("Mélange grande tuile (intensité)", "_AT_FarBlend", 0f, 1f),
            new Param("Échelle de la grande tuile", "_AT_FarScale", 0.05f, 0.6f),
            new Param("Début du mélange (m)", "_AT_FarStart", 0f, 800f),
            new Param("Fin du mélange (m)", "_AT_FarEnd", 1f, 1500f),
        };
        private static readonly Param[] TriParams =
        {
            new Param("Triplanar : intensité", "_AT_TriAmount", 0f, 1f),
            new Param("Début (normale.y, haut = pente douce)", "_AT_TriStart", 0f, 1f),
            new Param("Plein (normale.y, bas = pente raide)", "_AT_TriEnd", 0f, 1f),
        };
        private static readonly Param[] MacroParams =
        {
            new Param("Macro : intensité", "_AT_MacroAmount", 0f, 1f),
            new Param("Macro : variation de luminosité", "_AT_MacroValue", 0f, 1f),
            new Param("Macro : taille A (m)", "_AT_MacroScaleA", 5f, 300f),
            new Param("Macro : taille B (m)", "_AT_MacroScaleB", 20f, 1000f),
        };

        public static void Draw(Terrain terrain)
        {
            if (terrain == null) { EditorGUILayout.HelpBox("Sélectionne un terrain dans l'onglet Terrain.", MessageType.Info); return; }

            Material mat = terrain.materialTemplate;
            bool active = mat != null && mat.shader != null && mat.shader.name == ShaderName;

            DrawActivation(terrain, active);
            if (active) SyncTerrainSize(mat, terrain);
            if (!active) return;

            EditorGUILayout.Space();
            DrawGlobalParams(mat);
            EditorGUILayout.Space();
            DrawLayerStrengths(terrain.terrainData);
        }

        #region Activation
        private static void DrawActivation(Terrain terrain, bool active)
        {
            EditorGUILayout.HelpBox(
                "Casse la répétition des textures : échantillonnage stochastique (rotation et décalage aléatoires) près de la caméra, " +
                "mélange avec une grande tuile et variation de teinte à grande échelle au loin. " +
                "Chaque couche a sa force (0 = comportement standard, sans surcoût).", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(active))
                    if (GUILayout.Button("Activer le shader anti-répétition")) Activate(terrain);
                using (new EditorGUI.DisabledScope(!active))
                    if (GUILayout.Button("Revenir au shader URP standard")) Restore(terrain);
            }
        }

        private static void Activate(Terrain terrain)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null) { Debug.LogError("[EasyTerrain] Shader introuvable : " + ShaderName); return; }
                mat = new Material(shader);
                mat.EnableKeyword(KeywordPerPixelNormal);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            if (mat.HasProperty("_AT_TerrainSize")) mat.SetFloat("_AT_TerrainSize", terrain.terrainData.size.x);
            Undo.RecordObject(terrain, "Activer anti-répétition");
            terrain.materialTemplate = mat;
            EditorUtility.SetDirty(terrain);
        }

        private static void Restore(Terrain terrain)
        {
            var standard = AssetDatabase.LoadAssetAtPath<Material>(StandardMaterialPath);
            if (standard == null) { Debug.LogError("[EasyTerrain] Matériau URP standard introuvable : " + StandardMaterialPath); return; }
            Undo.RecordObject(terrain, "Désactiver anti-répétition");
            terrain.materialTemplate = standard;
            EditorUtility.SetDirty(terrain);
        }
        #endregion

        // Le shader a besoin de la taille X du terrain pour convertir les tuiles en mètres (triplanar).
        private static void SyncTerrainSize(Material mat, Terrain terrain)
        {
            if (!mat.HasProperty("_AT_TerrainSize")) return;
            float size = terrain.terrainData.size.x;
            if (Mathf.Approximately(mat.GetFloat("_AT_TerrainSize"), size)) return;
            Undo.RecordObject(mat, "Taille du terrain");
            mat.SetFloat("_AT_TerrainSize", size);
            EditorUtility.SetDirty(mat);
        }

        #region Paramètres globaux
        private static void DrawGlobalParams(Material mat)
        {
            EditorGUILayout.LabelField("Stochastique (proche)", EditorStyles.boldLabel);
            DrawParams(mat, StochasticParams);
            EditorGUILayout.LabelField("Grande tuile (loin)", EditorStyles.boldLabel);
            DrawParams(mat, FarParams);
            EditorGUILayout.LabelField("Parois raides (triplanar : supprime l'étirement)", EditorStyles.boldLabel);
            DrawParams(mat, TriParams);
            EditorGUILayout.LabelField("Variation macro (teinte / luminosité)", EditorStyles.boldLabel);
            DrawParams(mat, MacroParams);
            DrawColor(mat, "Teinte chaude", "_AT_MacroWarm");
            DrawColor(mat, "Teinte froide", "_AT_MacroCool");
        }

        private static void DrawParams(Material mat, Param[] list)
        {
            foreach (Param p in list)
            {
                if (!mat.HasProperty(p.property)) continue;
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.Slider(p.label, mat.GetFloat(p.property), p.min, p.max);
                if (!EditorGUI.EndChangeCheck()) continue;
                Undo.RecordObject(mat, "Anti-répétition : " + p.label);
                mat.SetFloat(p.property, value);
                EditorUtility.SetDirty(mat);
            }
        }

        private static void DrawColor(Material mat, string label, string property)
        {
            if (!mat.HasProperty(property)) return;
            EditorGUI.BeginChangeCheck();
            Color value = EditorGUILayout.ColorField(new GUIContent(label), mat.GetColor(property), true, false, true);
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(mat, "Anti-répétition : " + label);
            mat.SetColor(property, value);
            EditorUtility.SetDirty(mat);
        }
        #endregion

        #region Force par couche
        public static float GetStrength(TerrainLayer layer) => Mathf.Clamp01(1f - layer.maskMapRemapMax.z);

        public static void SetStrength(TerrainLayer layer, float strength)
        {
            Undo.RecordObject(layer, "Force anti-répétition");
            Vector4 max = layer.maskMapRemapMax;
            max.z = 1f - Mathf.Clamp01(strength);
            layer.maskMapRemapMax = max;
            EditorUtility.SetDirty(layer);
        }

        private static void DrawLayerStrengths(TerrainData data)
        {
            EditorGUILayout.LabelField("Force par couche", EditorStyles.boldLabel);
            TerrainLayer[] layers = data.terrainLayers;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Tout à 0")) SetAll(layers, 0f);
                if (GUILayout.Button("Tout à 1")) SetAll(layers, 1f);
            }

            for (int i = 0; i < layers.Length; i++)
            {
                TerrainLayer layer = layers[i];
                if (layer == null) continue;
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.Slider(i + " · " + layer.name, GetStrength(layer), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetStrength(layer, value);
            }
            AssetDatabase.SaveAssetIfDirty(data);
        }

        private static void SetAll(TerrainLayer[] layers, float value)
        {
            foreach (TerrainLayer layer in layers)
                if (layer != null) SetStrength(layer, value);
        }
        #endregion
    }
}
