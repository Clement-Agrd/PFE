#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DamageFx.Editor
{
    /// <summary>
    /// Tools > Damage FX :
    ///   1 Créer les assets par défaut  : icônes, matériaux TMP (contour + ombre + glow), styles, réglages, table de correspondance, prefab du chiffre
    ///   2 Ajouter le gestionnaire      : pose le DamageNumbersManager dans la scène ouverte (active le nouveau système)
    ///   3 Créer la scène de test       : horde de cibles qui prennent des coups en rafale, ciel clair + sol rouge, bloom
    /// </summary>
    public static class DamageFxMenu
    {
        private const string Root = "Assets/DamageFx";
        private const string FontsDir = Root + "/Fonts";
        private const string IconsDir = Root + "/Icons";
        private const string MaterialsDir = Root + "/Materials";
        private const string StylesDir = Root + "/Styles";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string SettingsPath = Root + "/DamageNumbersSettings.asset";
        private const string MapPath = Root + "/DamageFxKindMap.asset";
        private const string PrefabPath = PrefabsDir + "/DamageNumberView.prefab";
        private const string CritStylePath = StylesDir + "/Style_Crit.asset";
        private const string FallbackFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        #region 1 - Assets par défaut
        [MenuItem("Tools/Damage FX/1 Créer les assets par défaut")]
        public static void CreateDefaults()
        {
            foreach (var dir in new[] { Root, FontsDir, IconsDir, MaterialsDir, StylesDir, PrefabsDir }) EnsureFolder(dir);

            TMP_FontAsset font = FindFont(out string fontNote);
            if (font == null) { Debug.LogError("[DamageFx] Aucune police TMP trouvée. Importe les TMP Essentials (Window > TextMeshPro > Import TMP Essential Resources)."); return; }

            var settings = LoadOrCreate<DamageNumbersSettings>(SettingsPath);

            // Styles : kind, haut, bas, contour, échelle, durée, montée, priorité, préfixe, glow (couleur alpha 0 = aucun)
            Style(font, DamageFxKind.Physical, new Color(1f, 0.97f, 0.88f), new Color(0.95f, 0.8f, 0.58f), new Color(0.12f, 0.07f, 0.03f), 1f, 1f, 1f, 10, "", 1f, default);
            Style(font, DamageFxKind.Fire, new Color(1f, 0.92f, 0.35f), new Color(1f, 0.5f, 0.08f), new Color(0.28f, 0.07f, 0f), 1f, 1f, 1f, 10, "", 1f, default);
            Style(font, DamageFxKind.Ice, new Color(0.82f, 1f, 1f), new Color(0.2f, 0.8f, 1f), new Color(0.02f, 0.12f, 0.27f), 1f, 1f, 1f, 10, "", 1f, default);
            Style(font, DamageFxKind.Poison, new Color(0.88f, 1f, 0.3f), new Color(0.45f, 0.9f, 0.1f), new Color(0.07f, 0.2f, 0.02f), 0.9f, 1f, 0.9f, 8, "", 1f, default);
            Style(font, DamageFxKind.Magic, new Color(0.93f, 0.82f, 1f), new Color(0.66f, 0.4f, 1f), new Color(0.16f, 0.05f, 0.32f), 1f, 1f, 1f, 10, "", 1f, default);
            Style(font, DamageFxKind.Heal, new Color(0.82f, 1f, 0.82f), new Color(0.4f, 0.85f, 0.5f), new Color(0.03f, 0.2f, 0.08f), 1f, 1.5f, 0.6f, 20, "+", 1f, default);
            Style(font, DamageFxKind.TowerHit, Color.white, new Color(1f, 0.86f, 0.8f), new Color(0.42f, 0.02f, 0.02f), 1.4f, 1.4f, 0.8f, 30, "", 1.2f, new Color(1f, 1f, 1f, 0.5f));
            Style(font, null, new Color(1f, 0.96f, 0.62f), new Color(1f, 0.74f, 0.12f), new Color(0.32f, 0.13f, 0f), 1.15f, 1.7f, 1.2f, 40, "", 2.6f, new Color(1f, 0.75f, 0.2f, 0.7f));

            var map = LoadOrCreate<DamageFxKindMap>(MapPath);
            Sprite burst = DamageFxIconFactory.CreateBurst(IconsDir + "/Icon_Burst.png");
            Material spriteMat = LoadOrCreateSpriteMaterial();
            BuildPrefab(font, settings, burst, spriteMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DamageFx] Assets créés dans " + Root + ". Police : " + fontNote + "\nÉtape suivante : Tools > Damage FX > 2 Ajouter le gestionnaire à la scène (ou 3 Scène de test).");
            Selection.activeObject = settings;
        }

        private static TMP_FontAsset FindFont(out string note)
        {
            // Police du jeu : tout asset SDF placé dans Assets/DamageFx/Fonts (ex. "Pirata One SDF") est prioritaire.
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { FontsDir }))
            {
                var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (f != null) { note = f.name; return f; }
            }
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontPath);
            note = fallback != null ? "LiberationSans SDF (provisoire : place un asset SDF dans " + FontsDir + " puis relance l'outil)" : "aucune";
            return fallback;
        }

        private static void Style(TMP_FontAsset font, DamageFxKind? kind, Color top, Color bottom, Color outline, float scale, float life, float rise,
            int priority, string prefix, float hdrFace, Color glow)
        {
            string name = kind.HasValue ? kind.Value.ToString() : "Crit";
            string path = kind.HasValue ? StylesDir + "/Style_" + name + ".asset" : CritStylePath;
            bool isCrit = !kind.HasValue;
            DamageFxKind iconKind = kind ?? DamageFxKind.Physical;

            Sprite icon = isCrit
                ? DamageFxIconFactory.CreateCritIcon(IconsDir + "/Icon_Crit.png")
                : DamageFxIconFactory.CreateIcon(IconsDir + "/Icon_" + name + ".png", iconKind);
            Material mat = CreateTextMaterial(font, "M_Dmg_" + name, outline, hdrFace, glow);

            var style = LoadOrCreate<DamageTypeStyle>(path);
            style.EditorInit(iconKind, top, bottom, outline, mat, icon, scale, life, rise, priority, prefix, "");
            EditorUtility.SetDirty(style);
        }

        /// <summary>Matériau TMP : contour épais, ombre douce, glow optionnel ; HDR sur la face pour le crit (bloom).</summary>
        private static Material CreateTextMaterial(TMP_FontAsset font, string name, Color outline, float faceHdr, Color glow)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader overlay = Shader.Find("TextMeshPro/Distance Field Overlay");
            if (overlay == null) { Debug.LogError("[DamageFx] Shader TextMeshPro/Distance Field Overlay introuvable."); return null; }
            if (mat == null)
            {
                mat = new Material(font.material);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = overlay;
            mat.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);

            mat.SetColor(ShaderUtilities.ID_FaceColor, new Color(faceHdr, faceHdr, faceHdr, 1f));
            mat.SetColor(ShaderUtilities.ID_OutlineColor, outline);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);        // contour (moins imposant)
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.03f);
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);

            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.6f));   // ombre douce
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.3f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.4f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.25f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.55f);
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);

            if (glow.a > 0f)
            {
                mat.SetColor(ShaderUtilities.ID_GlowColor, glow);
                mat.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
                mat.SetFloat(ShaderUtilities.ID_GlowInner, 0.05f);
                mat.SetFloat(ShaderUtilities.ID_GlowOuter, 0.3f);
                mat.SetFloat(ShaderUtilities.ID_GlowPower, 0.55f);
                mat.EnableKeyword(ShaderUtilities.Keyword_Glow);
            }
            else mat.DisableKeyword(ShaderUtilities.Keyword_Glow);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material LoadOrCreateSpriteMaterial()
        {
            string path = MaterialsDir + "/M_DamageFxSprite.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("DamageFx/Sprite"));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void BuildPrefab(TMP_FontAsset font, DamageNumbersSettings settings, Sprite burstSprite, Material spriteMat)
        {
            var root = new GameObject("DamageNumberView");
            var view = root.AddComponent<DamageNumberView>();

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            var label = labelGo.AddComponent<TextMeshPro>();
            label.font = font;
            label.fontSize = 10f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = false;
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(20f, 5f);
            label.sortingOrder = 5;
            var labelRenderer = labelGo.GetComponent<MeshRenderer>();
            labelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            labelRenderer.receiveShadows = false;

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(labelGo.transform, false);
            var icon = iconGo.AddComponent<SpriteRenderer>();
            icon.sharedMaterial = spriteMat;
            icon.sortingOrder = 4;
            icon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            icon.receiveShadows = false;
            icon.enabled = false;

            var burstGo = new GameObject("Burst");
            burstGo.transform.SetParent(root.transform, false);
            var burst = burstGo.AddComponent<SpriteRenderer>();
            burst.sprite = burstSprite;
            burst.sharedMaterial = spriteMat;
            burst.sortingOrder = 3;
            burst.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            burst.receiveShadows = false;
            burst.enabled = false;

            var so = new SerializedObject(view);
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("burst").objectReferenceValue = burst;
            so.FindProperty("settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }
        #endregion

        #region 2 - Gestionnaire dans la scène
        [MenuItem("Tools/Damage FX/2 Ajouter le gestionnaire à la scène")]
        public static void AddManagerToScene()
        {
            if (AssetDatabase.LoadAssetAtPath<DamageNumbersSettings>(SettingsPath) == null) CreateDefaults();
            var existing = Object.FindFirstObjectByType<DamageNumbersManager>();
            if (existing != null) { Selection.activeObject = existing; Debug.Log("[DamageFx] Un gestionnaire existe déjà dans la scène."); return; }
            var go = CreateManager();
            Undo.RegisterCreatedObjectUndo(go, "DamageNumbersManager");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeObject = go;
            Debug.Log("[DamageFx] Gestionnaire ajouté : le nouveau système remplace l'ancien dans cette scène (supprime l'objet pour revenir à l'ancien).");
        }

        private static GameObject CreateManager()
        {
            var go = new GameObject("DamageNumbersManager");
            var manager = go.AddComponent<DamageNumbersManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DamageNumbersSettings>(SettingsPath);
            so.FindProperty("viewPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DamageNumberView>(PrefabPath);
            so.FindProperty("kindMap").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DamageFxKindMap>(MapPath);
            so.FindProperty("critStyle").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DamageTypeStyle>(CritStylePath);
            SerializedProperty styles = so.FindProperty("styles");
            styles.arraySize = DamageFxKinds.Count;
            for (int i = 0; i < DamageFxKinds.Count; i++)
                styles.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<DamageTypeStyle>(StylesDir + "/Style_" + ((DamageFxKind)i) + ".asset");
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }
        #endregion

        #region 3 - Scène de test
        [MenuItem("Tools/Damage FX/3 Créer la scène de test")]
        public static void CreateTestScene()
        {
            if (AssetDatabase.LoadAssetAtPath<DamageNumbersSettings>(SettingsPath) == null) CreateDefaults();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Ciel clair + sol rouge : les pires fonds pour la lisibilité.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Sol_Rouge";
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            ground.GetComponent<Renderer>().sharedMaterial = LoadOrCreateLit(MaterialsDir + "/M_TestGround.mat", new Color(0.55f, 0.1f, 0.08f));

            Material targetMat = LoadOrCreateLit(MaterialsDir + "/M_TestTarget.mat", new Color(0.35f, 0.3f, 0.28f));
            var horde = new GameObject("Horde").transform;
            var targets = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < 36; i++)
            {
                float ring = 3.5f + (i % 3) * 2.6f;
                float angle = i * 137.5f * Mathf.Deg2Rad;
                var t = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                t.name = "Cible_" + i;
                t.transform.SetParent(horde, false);
                t.transform.position = new Vector3(Mathf.Cos(angle) * ring, 1f, 4f + Mathf.Sin(angle) * ring * 0.8f);
                t.GetComponent<Renderer>().sharedMaterial = targetMat;
                Object.DestroyImmediate(t.GetComponent<Collider>());
                targets.Add(t.transform);
            }

            var tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tower.name = "Ma_Tour (DamageFxTag Tower)";
            tower.transform.position = new Vector3(0f, 2f, 15f);
            tower.transform.localScale = new Vector3(2.5f, 4f, 2.5f);
            tower.GetComponent<Renderer>().sharedMaterial = targetMat;
            SetRole(tower.AddComponent<DamageFxTag>(), DamageFxRole.Tower);

            var boss = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            boss.name = "Boss (DamageFxTag Boss)";
            boss.transform.position = new Vector3(6f, 1.8f, 12f);
            boss.transform.localScale = new Vector3(2f, 1.8f, 2f);
            boss.GetComponent<Renderer>().sharedMaterial = targetMat;
            SetRole(boss.AddComponent<DamageFxTag>(), DamageFxRole.Boss);

            var managerGo = CreateManager();
            var testGo = new GameObject("DamageFxStressTest");
            var test = testGo.AddComponent<DamageFxStressTest>();
            var so = new SerializedObject(test);
            so.FindProperty("tower").objectReferenceValue = tower.transform;
            so.FindProperty("boss").objectReferenceValue = boss.transform;
            so.FindProperty("manager").objectReferenceValue = managerGo.GetComponent<DamageNumbersManager>();
            SerializedProperty arr = so.FindProperty("targets");
            arr.arraySize = targets.Count;
            for (int i = 0; i < targets.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 8f, -8f);
                cam.transform.LookAt(new Vector3(0f, 1f, 7f));
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            }

            AddBloomVolume();

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/DamageFxTest.unity");
            Debug.Log("[DamageFx] Scène de test créée : Assets/Scenes/DamageFxTest.unity. Lance le Play : la horde prend des coups en rafale (Espace = intensité).");
        }

        private static void SetRole(DamageFxTag tag, DamageFxRole role)
        {
            var so = new SerializedObject(tag);
            so.FindProperty("role").enumValueIndex = (int)role;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddBloomVolume()
        {
            string path = Root + "/DamageFxTestVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(1f);
                bloom.intensity.Override(0.8f);
                bloom.scatter.Override(0.6f);
                AssetDatabase.AddObjectToAsset(bloom, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            var go = new GameObject("Volume_Bloom");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static Material LoadOrCreateLit(string path, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
        #endregion

        #region Utilitaires
        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        #endregion
    }
}
#endif
