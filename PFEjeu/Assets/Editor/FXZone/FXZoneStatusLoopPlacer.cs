#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Core.FXZone.Editor
{
    /// <summary>
    /// Pose dans ZONE_07_FX trois mannequins qui rejouent en boucle les états Soin, Brûlure et Ralenti (gel),
    /// en réutilisant les prefabs existants (HealPulse_Ally, BurnTarget, FrozenTarget) et leurs LoopDemo.
    /// Ne modifie aucun prefab ni aucun script du projet : tout est créé sous un seul objet "Etats_Loop".
    ///
    /// ZONE_07_FX
    /// └── Etats_Loop
    ///     ├── Etat_Soin      (Socle, Corps, Tete, HealPulse_Ally  + HealTargetLoopDemo)
    ///     ├── Etat_Brulure   (Socle, Corps, Tete, BurnTarget      + BurnTargetLoopDemo)
    ///     └── Etat_Ralenti   (Socle, Corps, Tete, FrozenTarget    + FrozenTargetLoopDemo)
    /// </summary>
    public static class FXZoneStatusLoopPlacer
    {
        #region Constantes
        private const string ZoneName = "ZONE_07_FX";
        private const string GroupName = "Etats_Loop";
        private const float Spacing = 3f;

        // Décalage local par rapport à ZONE_07_FX : à ajuster ensuite en déplaçant simplement "Etats_Loop".
        private static readonly Vector3 GroupLocalOffset = new Vector3(0f, 0f, -8f);
        #endregion

        [MenuItem("Tools/FX Zone/Placer les états en boucle (Soin, Brûlure, Ralenti)")]
        public static void Place()
        {
            GameObject zone = GameObject.Find(ZoneName);
            if (zone == null)
            {
                Debug.LogError($"[FXZone] {ZoneName} introuvable dans la scène ouverte (ouvre SceneTrainingRoom).");
                return;
            }

            GameObject healPrefab = FindPrefab("HealPulse_Ally");
            GameObject burnPrefab = FindPrefab("BurnTarget");
            GameObject freezePrefab = FindPrefab("FrozenTarget");
            if (healPrefab == null || burnPrefab == null || freezePrefab == null)
            {
                Debug.LogError("[FXZone] Prefab manquant : HealPulse_Ally, BurnTarget ou FrozenTarget (voir Tools > Heal FX / Burn FX / Freeze FX).");
                return;
            }

            // Idempotent : on remplace uniquement le groupe créé par cet outil.
            Transform existing = zone.transform.Find(GroupName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var group = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(group, "Etats_Loop");
            group.transform.SetParent(zone.transform, false);
            group.transform.localPosition = GroupLocalOffset;

            Transform heal = CreateMannequin(group.transform, "Etat_Soin", "SOIN", new Vector3(-Spacing, 0f, 0f));
            Transform burn = CreateMannequin(group.transform, "Etat_Brulure", "BRÛLURE", Vector3.zero);
            Transform slow = CreateMannequin(group.transform, "Etat_Ralenti", "RALENTI", new Vector3(Spacing, 0f, 0f));

            // Soin : le prefab est un enfant posé aux pieds (même décalage que le prefab TrainingRoom_HealFx).
            var healFx = (GameObject)PrefabUtility.InstantiatePrefab(healPrefab, heal);
            healFx.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            heal.gameObject.AddComponent<HealFx.HealTargetLoopDemo>().Bind(healFx.GetComponent<HealFx.HealTargetFx>());

            // Brûlure : BurnTargetFx mesure le corps du mannequin au Awake (autoFit).
            var burnFx = (GameObject)PrefabUtility.InstantiatePrefab(burnPrefab, burn);
            burnFx.transform.localPosition = Vector3.zero;
            burn.gameObject.AddComponent<BurnFx.BurnTargetLoopDemo>().Bind(burnFx.GetComponent<BurnFx.BurnTargetFx>());

            // Ralenti : même FX de gel que l'état Tower_Frost (modifs de stats négatifs).
            var freezeFx = (GameObject)PrefabUtility.InstantiatePrefab(freezePrefab, slow);
            freezeFx.transform.localPosition = Vector3.zero;
            slow.gameObject.AddComponent<FreezeFx.FrozenTargetLoopDemo>().Bind(freezeFx.GetComponent<FreezeFx.FrozenTargetFx>());

            EditorSceneManager.MarkSceneDirty(group.scene);
            Selection.activeGameObject = group;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("[FXZone] Etats_Loop posé sous ZONE_07_FX : Soin, Brûlure, Ralenti en boucle (en Play). Déplace 'Etats_Loop' pour le repositionner.");
        }

        #region Construction
        private static Transform CreateMannequin(Transform parent, string objectName, string label, Vector3 localPosition)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            AddPrimitive(root.transform, PrimitiveType.Cylinder, "Socle", new Vector3(0f, 0.04f, 0f), new Vector3(0.9f, 0.04f, 0.9f));
            AddPrimitive(root.transform, PrimitiveType.Capsule, "Corps", new Vector3(0f, 0.95f, 0f), new Vector3(0.5f, 0.9f, 0.5f));
            AddPrimitive(root.transform, PrimitiveType.Sphere, "Tete", new Vector3(0f, 2.0f, 0f), new Vector3(0.38f, 0.38f, 0.38f));
            AddLabel(root.transform, label);
            return root.transform;
        }

        private static void AddPrimitive(Transform parent, PrimitiveType type, string partName, Vector3 localPosition, Vector3 localScale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
        }

        private static void AddLabel(Transform parent, string text)
        {
            var go = new GameObject("Etiquette");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 2.9f, 0f);

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = 0.12f;
            mesh.fontSize = 48;
            mesh.color = Color.white;
        }
        #endregion

        #region Recherche d'assets
        private static GameObject FindPrefab(string prefabName)
        {
            foreach (string guid in AssetDatabase.FindAssets(prefabName + " t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == prefabName)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }
        #endregion
    }
}
#endif
