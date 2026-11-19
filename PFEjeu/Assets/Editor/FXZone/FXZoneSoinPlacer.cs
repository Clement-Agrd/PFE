#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZoneFx;

namespace Core.FXZone.Editor
{
    /// <summary>
    /// Ajoute la baie "Zone_Soin" à ZONE_07_FX : cadre au sol, murs, titre, repère Cam_Clip (pour FXViewer),
    /// une instance de ZoneFx_Flat et le script de démo ZoneFxHealDemo. Idempotent : une Zone_Soin existante est recréée.
    /// Prérequis : Tools > Zone FX > 1 Installer (crée le prefab ZoneFx_Flat).
    /// </summary>
    public static class FXZoneSoinPlacer
    {
        private const string PrefabPath = "Assets/VFX/ZoneFx/Prefabs/ZoneFx_Flat.prefab";
        private const string BorderMatPath = "Assets/Materials/FXZone_Zone_Soin.mat";
        private const string WallMatPath = "Assets/Materials/FXZone_Mur.mat";
        private const string ZoneName = "Zone_Soin";
        private const float SoftIntensity = 0.55f;
        private const float SoftHdrIntensity = 1.3f;
        private const float SoftVeilIntensity = 0.5f;

        [MenuItem("Tools/FX Zone/Ajouter zone Soin (ZoneFx)")]
        public static void Place()
        {
            GameObject rootGo = GameObject.Find("ZONE_07_FX");
            if (rootGo == null) { Debug.LogError("[FXZone] ZONE_07_FX introuvable dans la scène ouverte."); return; }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[FXZone] Prefab introuvable : " + PrefabPath + ". Lance d'abord Tools > Zone FX > 1 Installer.");
                return;
            }

            Transform root = rootGo.transform;
            Transform old = root.Find(ZoneName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // Même gabarit que les autres baies (12 x 20 m), à la suite de Zone_Desintegration (x = 151,5).
            const float sx = 12f, sz = 20f, cz = 79.75f, cx = 164.5f;
            Color col = new Color(.3f, 1f, .45f);

            var zone = new GameObject(ZoneName);
            Undo.RegisterCreatedObjectUndo(zone, "Zone Soin");
            zone.transform.SetParent(root, false);
            zone.transform.position = new Vector3(cx, 0f, cz);

            Material border = LoadOrCreate(BorderMatPath, "Universal Render Pipeline/Unlit", col);
            Material wall = LoadOrCreate(WallMatPath, "Universal Render Pipeline/Lit", new Color(.16f, .17f, .2f));

            BuildBorder(zone.transform, border, cx, cz, sx, sz);
            BuildWalls(zone.transform, wall, cx, cz, sx, sz);

            var label = new GameObject("Titre");
            label.transform.SetParent(zone.transform, false);
            label.transform.position = new Vector3(cx, 0.1f, cz - sz / 2f + 0.8f);
            label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var tm = label.AddComponent<TextMesh>();
            tm.text = "SOIN";
            tm.anchor = TextAnchor.MiddleCenter;
            tm.fontSize = 64;
            tm.characterSize = 0.08f;
            tm.color = col;

            // Repère caméra de FXViewer : au sud de la baie, en hauteur, regarde vers le centre.
            var cam = new GameObject("Cam_Clip");
            cam.transform.SetParent(zone.transform, false);
            cam.transform.position = new Vector3(cx, 6f, cz - sz / 2f + 2f);
            cam.transform.LookAt(new Vector3(cx, 0.5f, cz));

            // L'effet : ZoneFx_Flat en enfant, pivot au sol.
            var fx = (GameObject)PrefabUtility.InstantiatePrefab(prefab, zone.transform);
            Undo.RegisterCreatedObjectUndo(fx, "ZoneFx_Flat");
            fx.transform.position = new Vector3(cx, 0f, cz);

            // Rendu adouci (moins agressif pour les yeux) : intensité globale, force HDR (bloom) et voile réduits.
            var fxSo = new SerializedObject(fx.GetComponent<ZoneFxController>());
            fxSo.FindProperty("intensity").floatValue = SoftIntensity;
            fxSo.FindProperty("hdrIntensity").floatValue = SoftHdrIntensity;
            fxSo.FindProperty("veilIntensity").floatValue = SoftVeilIntensity;
            fxSo.ApplyModifiedPropertiesWithoutUndo();

            var demo = zone.AddComponent<ZoneFxHealDemo>();
            var so = new SerializedObject(demo);
            so.FindProperty("zone").objectReferenceValue = fx.GetComponent<ZoneFxController>();
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = zone;
            Debug.Log("[FXZone] Zone_Soin ajoutée. Z = afficher/masquer, X = preset, C = rayon (en Play).");
        }

        private static Material LoadOrCreate(string path, string shaderName, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find(shaderName));
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void BuildBorder(Transform parent, Material mat, float cx, float cz, float sx, float sz)
        {
            var border = new GameObject("Delimitation");
            border.transform.SetParent(parent, false);
            const float t = 0.2f, y = 0.03f;
            Strip(border.transform, mat, "Nord", new Vector3(cx, y, cz + sz / 2f), new Vector3(sx + t, 0.06f, t));
            Strip(border.transform, mat, "Sud", new Vector3(cx, y, cz - sz / 2f), new Vector3(sx + t, 0.06f, t));
            Strip(border.transform, mat, "Est", new Vector3(cx + sx / 2f, y, cz), new Vector3(t, 0.06f, sz));
            Strip(border.transform, mat, "Ouest", new Vector3(cx - sx / 2f, y, cz), new Vector3(t, 0.06f, sz));
        }

        private static void Strip(Transform parent, Material mat, string name, Vector3 pos, Vector3 scale)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(s.GetComponent<BoxCollider>());
            s.name = name;
            s.transform.SetParent(parent, false);
            s.transform.position = pos;
            s.transform.localScale = scale;
            var r = s.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // Murs posés sur le cadre, passage de 3 m au milieu du mur Sud (comme les autres baies).
        private static void BuildWalls(Transform parent, Material mat, float cx, float cz, float sx, float sz)
        {
            var walls = new GameObject("Murs");
            walls.transform.SetParent(parent, false);
            const float wt = 0.3f, wh = 10f, gap = 3f, wy = wh / 2f;
            Wall(walls.transform, mat, "Mur_Nord", new Vector3(cx, wy, cz + sz / 2f), new Vector3(sx + wt, wh, wt));
            Wall(walls.transform, mat, "Mur_Est", new Vector3(cx + sx / 2f, wy, cz), new Vector3(wt, wh, sz));
            Wall(walls.transform, mat, "Mur_Ouest", new Vector3(cx - sx / 2f, wy, cz), new Vector3(wt, wh, sz));
            float half = (sx + wt - gap) / 2f;
            Wall(walls.transform, mat, "Mur_Sud_Gauche", new Vector3(cx - sx / 2f - wt / 2f + half / 2f, wy, cz - sz / 2f), new Vector3(half, wh, wt));
            Wall(walls.transform, mat, "Mur_Sud_Droit", new Vector3(cx + sx / 2f + wt / 2f - half / 2f, wy, cz - sz / 2f), new Vector3(half, wh, wt));
        }

        private static void Wall(Transform parent, Material mat, string name, Vector3 pos, Vector3 scale)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = name;
            w.transform.SetParent(parent, false);
            w.transform.position = pos;
            w.transform.localScale = scale;
            w.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
#endif
