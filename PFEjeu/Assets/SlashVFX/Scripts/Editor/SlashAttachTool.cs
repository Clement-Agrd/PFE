using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ProfessionalTPS;

namespace SlashVFX.EditorTools
{
    /// <summary>Adds the slash VFX (controller, bridge, adapter) under the character that owns a MeleeCombatModule.</summary>
    public static class SlashAttachTool
    {
        [MenuItem("Tools/Slash VFX/Attach To Melee Player")]
        public static void AttachToMeleePlayer()
        {
            MeleeCombatModule melee = Object.FindFirstObjectByType<MeleeCombatModule>();
            if (melee == null)
            {
                Debug.LogWarning("[SlashVFX] No MeleeCombatModule in the open scene.");
                return;
            }

            Transform root = melee.transform;
            var cc = melee.GetComponentInParent<CharacterController>();
            if (cc != null) root = cc.transform;

            Transform fx = root.Find("SlashFX");
            if (fx == null)
            {
                fx = new GameObject("SlashFX").transform;
                fx.SetParent(root, false);
                Undo.RegisterCreatedObjectUndo(fx.gameObject, "Add SlashFX");
            }

            var ctrl = fx.GetComponent<SlashComboController>();
            if (ctrl == null) ctrl = fx.gameObject.AddComponent<SlashComboController>();
            string[] names = { "Step1_Combo1", "Step2_Combo2", "Step3_Combo3", "Step4_Finisher" };
            var steps = new SlashComboStep[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                steps[i] = AssetDatabase.LoadAssetAtPath<SlashComboStep>("Assets/SlashVFX/Steps/" + names[i] + ".asset");
                if (steps[i] == null) Debug.LogWarning("[SlashVFX] Missing step asset " + names[i]);
            }
            ctrl.steps = steps;
            ctrl.readMouse = false; // the melee module owns the input
            ctrl.origin = root;
            foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.isGlobal) { ctrl.bloomVolume = v; break; }

            var bridge = fx.GetComponent<SlashMeleeBridge>();
            if (bridge == null) bridge = fx.gameObject.AddComponent<SlashMeleeBridge>();
            bridge.controller = ctrl;
            bridge.chestHeight = 1.1f;

            var adapter = fx.GetComponent<SlashMeleeAdapter>();
            if (adapter == null) adapter = fx.gameObject.AddComponent<SlashMeleeAdapter>();
            adapter.melee = melee;
            adapter.bridge = bridge;

            EditorUtility.SetDirty(fx.gameObject);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log($"[SlashVFX] Attached to '{root.name}' (melee on '{melee.name}'), bloom volume: {(ctrl.bloomVolume != null)}");
        }
    }
}
