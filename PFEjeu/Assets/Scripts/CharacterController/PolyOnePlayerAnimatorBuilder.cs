#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ProfessionalTPS.EditorTools
{
    /// <summary>
    /// Construit un Animator Controller joueur à partir du pack PolyOne.
    ///
    /// Menu :
    /// Tools > PFE > Player > Build PolyOne Animator
    ///
    /// Le contrôleur utilise :
    /// - locomotion libre : Idle / Walk / Run / Run Fast
    /// - locomotion directionnelle : Run Left / Right / Backward / Forward
    /// - Jumping Up / Jumping Down
    /// - Tripping comme animation temporaire de Roll
    ///
    /// Les paramètres de combat existent déjà afin de rester compatibles
    /// avec PlayerAnimationBridge, même si PolyOne ne fournit pas encore
    /// les clips d'attaque épée / arc / magie.
    /// </summary>
    public static class PolyOnePlayerAnimatorBuilder
    {
        private const string OutputFolder =
            "Assets/Animations/Player";

        private const string ControllerPath =
            OutputFolder +
            "/PFE_PolyOne_Player.controller";

        private const string PolyOneAnimationFolder =
            "Assets/PolyOne/Basic Motions/Animation";


        // ============================================================
        // MENU
        // ============================================================

        [MenuItem(
            "Tools/PFE/Player/Build PolyOne Animator"
        )]
        public static void Build()
        {
            EnsureOutputFolder();

            Dictionary<string, AnimationClip> clips =
                LoadRequiredClips();

            if (clips == null)
                return;

            ConfigureLooping(
                clips
            );

            if (AssetDatabase.LoadAssetAtPath<
                    AnimatorController
                >(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(
                    ControllerPath
                );
            }

            AnimatorController controller =
                AnimatorController
                    .CreateAnimatorControllerAtPath(
                        ControllerPath
                    );

            AddParameters(
                controller
            );

            AnimatorStateMachine stateMachine =
                controller.layers[0]
                    .stateMachine;

            // --------------------------------------------------------
            // FREE LOCOMOTION
            // --------------------------------------------------------

            BlendTree freeLocomotion =
                new BlendTree
                {
                    name =
                        "Free Locomotion Blend",
                    blendType =
                        BlendTreeType.Simple1D,
                    blendParameter =
                        "Speed",
                    useAutomaticThresholds =
                        false
                };

            AssetDatabase.AddObjectToAsset(
                freeLocomotion,
                controller
            );

            freeLocomotion.AddChild(
                clips["Idle"],
                0f
            );

            freeLocomotion.AddChild(
                clips["Walk"],
                0.25f
            );

            freeLocomotion.AddChild(
                clips["Run"],
                0.65f
            );

            freeLocomotion.AddChild(
                clips["Run Fast"],
                1f
            );


            AnimatorState freeState =
                stateMachine.AddState(
                    "Free Locomotion",
                    new Vector3(
                        300f,
                        100f,
                        0f
                    )
                );

            freeState.motion =
                freeLocomotion;

            stateMachine.defaultState =
                freeState;


            // --------------------------------------------------------
            // DIRECTIONAL / STRAFE LOCOMOTION
            // --------------------------------------------------------

            BlendTree directionalLocomotion =
                new BlendTree
                {
                    name =
                        "Directional Locomotion Blend",
                    blendType =
                        BlendTreeType.SimpleDirectional2D,
                    blendParameter =
                        "MoveX",
                    blendParameterY =
                        "MoveY"
                };

            AssetDatabase.AddObjectToAsset(
                directionalLocomotion,
                controller
            );

            directionalLocomotion.AddChild(
                clips["Idle"],
                Vector2.zero
            );

            directionalLocomotion.AddChild(
                clips["Run"],
                new Vector2(
                    0f,
                    1f
                )
            );

            directionalLocomotion.AddChild(
                clips["Run Backward"],
                new Vector2(
                    0f,
                    -1f
                )
            );

            directionalLocomotion.AddChild(
                clips["Run Left"],
                new Vector2(
                    -1f,
                    0f
                )
            );

            directionalLocomotion.AddChild(
                clips["Run Right"],
                new Vector2(
                    1f,
                    0f
                )
            );


            AnimatorState directionalState =
                stateMachine.AddState(
                    "Directional Locomotion",
                    new Vector3(
                        580f,
                        100f,
                        0f
                    )
                );

            directionalState.motion =
                directionalLocomotion;


            AddBoolTransition(
                freeState,
                directionalState,
                "DirectionalLocomotion",
                true,
                0.08f
            );

            AddBoolTransition(
                directionalState,
                freeState,
                "DirectionalLocomotion",
                false,
                0.08f
            );


            // --------------------------------------------------------
            // JUMP
            // --------------------------------------------------------

            AnimatorState jumpUpState =
                stateMachine.AddState(
                    "Jump Up",
                    new Vector3(
                        430f,
                        -80f,
                        0f
                    )
                );

            jumpUpState.motion =
                clips["Jumping Up"];


            AnimatorState jumpDownState =
                stateMachine.AddState(
                    "Jump Down",
                    new Vector3(
                        430f,
                        -220f,
                        0f
                    )
                );

            jumpDownState.motion =
                clips["Jumping Down"];


            AnimatorStateTransition jumpTrigger =
                stateMachine
                    .AddAnyStateTransition(
                        jumpUpState
                    );

            ConfigureImmediateTransition(
                jumpTrigger,
                0.05f
            );

            jumpTrigger.canTransitionToSelf =
                false;

            jumpTrigger.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "Jump"
            );


            AnimatorStateTransition jumpApex =
                jumpUpState.AddTransition(
                    jumpDownState
                );

            ConfigureImmediateTransition(
                jumpApex,
                0.08f
            );

            jumpApex.AddCondition(
                AnimatorConditionMode.Less,
                0.01f,
                "VerticalSpeed"
            );


            AddGroundReturnTransition(
                jumpDownState,
                freeState,
                false
            );

            AddGroundReturnTransition(
                jumpDownState,
                directionalState,
                true
            );


            // --------------------------------------------------------
            // ROLL
            // --------------------------------------------------------

            AnimatorState rollState =
                stateMachine.AddState(
                    "Roll (Tripping Temp)",
                    new Vector3(
                        760f,
                        -100f,
                        0f
                    )
                );

            rollState.motion =
                clips["Tripping"];


            AnimatorStateTransition rollTrigger =
                stateMachine
                    .AddAnyStateTransition(
                        rollState
                    );

            ConfigureImmediateTransition(
                rollTrigger,
                0.03f
            );

            rollTrigger.canTransitionToSelf =
                false;

            rollTrigger.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "Roll"
            );


            AddExitReturnTransition(
                rollState,
                freeState,
                false
            );

            AddExitReturnTransition(
                rollState,
                directionalState,
                true
            );


            EditorUtility.SetDirty(
                controller
            );

            AssetDatabase.SaveAssets();

            AssetDatabase.Refresh();

            AssignToSelectedPlayer(
                controller
            );

            Selection.activeObject =
                controller;

            EditorGUIUtility.PingObject(
                controller
            );

            Debug.Log(
                "[PolyOne Animator] Controller créé : " +
                ControllerPath +
                ". Les attaques de combat restent prêtes côté paramètres, " +
                "mais attendent de vrais clips d'attaque."
            );
        }


        // ============================================================
        // PARAMETERS
        // ============================================================

        private static void AddParameters(
            AnimatorController controller)
        {
            AddFloat(
                controller,
                "Speed",
                0f
            );

            AddFloat(
                controller,
                "MoveX",
                0f
            );

            AddFloat(
                controller,
                "MoveY",
                0f
            );

            AddFloat(
                controller,
                "VerticalSpeed",
                0f
            );

            AddBool(
                controller,
                "Grounded",
                true
            );

            AddBool(
                controller,
                "Aiming",
                false
            );

            AddBool(
                controller,
                "Sprinting",
                false
            );

            AddBool(
                controller,
                "DirectionalLocomotion",
                false
            );

            AddInt(
                controller,
                "CombatMode",
                0
            );

            AddFloat(
                controller,
                "AttackSpeed",
                1f
            );

            AddBool(
                controller,
                "BowDrawing",
                false
            );

            AddFloat(
                controller,
                "BowCharge",
                0f
            );

            AddTrigger(
                controller,
                "Jump"
            );

            AddTrigger(
                controller,
                "Roll"
            );

            AddTrigger(
                controller,
                "Melee1"
            );

            AddTrigger(
                controller,
                "Melee2"
            );

            AddTrigger(
                controller,
                "Melee3"
            );

            AddTrigger(
                controller,
                "BowAttack"
            );

            AddTrigger(
                controller,
                "MagicAttack"
            );

            AddTrigger(
                controller,
                "AxeAttack"
            );

            AddTrigger(
                controller,
                "PickaxeAttack"
            );
        }


        private static void AddFloat(
            AnimatorController controller,
            string name,
            float defaultValue)
        {
            controller.AddParameter(
                new AnimatorControllerParameter
                {
                    name = name,
                    type =
                        AnimatorControllerParameterType.Float,
                    defaultFloat =
                        defaultValue
                }
            );
        }


        private static void AddBool(
            AnimatorController controller,
            string name,
            bool defaultValue)
        {
            controller.AddParameter(
                new AnimatorControllerParameter
                {
                    name = name,
                    type =
                        AnimatorControllerParameterType.Bool,
                    defaultBool =
                        defaultValue
                }
            );
        }


        private static void AddInt(
            AnimatorController controller,
            string name,
            int defaultValue)
        {
            controller.AddParameter(
                new AnimatorControllerParameter
                {
                    name = name,
                    type =
                        AnimatorControllerParameterType.Int,
                    defaultInt =
                        defaultValue
                }
            );
        }


        private static void AddTrigger(
            AnimatorController controller,
            string name)
        {
            controller.AddParameter(
                name,
                AnimatorControllerParameterType.Trigger
            );
        }


        // ============================================================
        // TRANSITIONS
        // ============================================================

        private static void AddBoolTransition(
            AnimatorState from,
            AnimatorState to,
            string parameter,
            bool value,
            float duration)
        {
            AnimatorStateTransition transition =
                from.AddTransition(
                    to
                );

            ConfigureImmediateTransition(
                transition,
                duration
            );

            transition.AddCondition(
                value
                    ? AnimatorConditionMode.If
                    : AnimatorConditionMode.IfNot,
                0f,
                parameter
            );
        }


        private static void AddGroundReturnTransition(
            AnimatorState from,
            AnimatorState to,
            bool directional)
        {
            AnimatorStateTransition transition =
                from.AddTransition(
                    to
                );

            ConfigureImmediateTransition(
                transition,
                0.08f
            );

            transition.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "Grounded"
            );

            transition.AddCondition(
                directional
                    ? AnimatorConditionMode.If
                    : AnimatorConditionMode.IfNot,
                0f,
                "DirectionalLocomotion"
            );
        }


        private static void AddExitReturnTransition(
            AnimatorState from,
            AnimatorState to,
            bool directional)
        {
            AnimatorStateTransition transition =
                from.AddTransition(
                    to
                );

            transition.hasExitTime =
                true;

            transition.exitTime =
                0.9f;

            transition.hasFixedDuration =
                true;

            transition.duration =
                0.08f;

            transition.AddCondition(
                directional
                    ? AnimatorConditionMode.If
                    : AnimatorConditionMode.IfNot,
                0f,
                "DirectionalLocomotion"
            );
        }


        private static void ConfigureImmediateTransition(
            AnimatorStateTransition transition,
            float duration)
        {
            transition.hasExitTime =
                false;

            transition.hasFixedDuration =
                true;

            transition.duration =
                duration;
        }


        // ============================================================
        // CLIPS
        // ============================================================

        private static Dictionary<
            string,
            AnimationClip
        > LoadRequiredClips()
        {
            string[] names =
            {
                "Idle",
                "Walk",
                "Run",
                "Run Fast",
                "Run Left",
                "Run Right",
                "Run Backward",
                "Jumping Up",
                "Jumping Down",
                "Tripping"
            };

            Dictionary<
                string,
                AnimationClip
            > clips =
                new Dictionary<
                    string,
                    AnimationClip
                >();

            foreach (string name in names)
            {
                string path =
                    PolyOneAnimationFolder +
                    "/" +
                    name +
                    ".anim";

                AnimationClip clip =
                    AssetDatabase.LoadAssetAtPath<
                        AnimationClip
                    >(path);

                if (clip == null)
                {
                    Debug.LogError(
                        "[PolyOne Animator] Clip introuvable : " +
                        path
                    );

                    return null;
                }

                clips[name] =
                    clip;
            }

            return clips;
        }


        private static void ConfigureLooping(
            Dictionary<
                string,
                AnimationClip
            > clips)
        {
            string[] loopingClips =
            {
                "Idle",
                "Walk",
                "Run",
                "Run Fast",
                "Run Left",
                "Run Right",
                "Run Backward"
            };

            foreach (
                string name
                in loopingClips)
            {
                SetClipLooping(
                    clips[name],
                    true
                );
            }

            SetClipLooping(
                clips["Jumping Up"],
                false
            );

            SetClipLooping(
                clips["Jumping Down"],
                false
            );

            SetClipLooping(
                clips["Tripping"],
                false
            );
        }


        private static void SetClipLooping(
            AnimationClip clip,
            bool loop)
        {
            if (clip == null)
                return;

            clip.wrapMode =
                loop
                    ? WrapMode.Loop
                    : WrapMode.Once;

            SerializedObject serializedClip =
                new SerializedObject(
                    clip
                );

            SerializedProperty settings =
                serializedClip.FindProperty(
                    "m_AnimationClipSettings"
                );

            SerializedProperty loopTime =
                settings != null
                    ? settings.FindPropertyRelative(
                        "m_LoopTime"
                    )
                    : null;

            if (loopTime != null)
            {
                loopTime.boolValue =
                    loop;
            }

            serializedClip
                .ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(
                clip
            );
        }


        // ============================================================
        // PLAYER ASSIGNMENT
        // ============================================================

        private static void AssignToSelectedPlayer(
            AnimatorController controller)
        {
            GameObject selected =
                Selection.activeGameObject;

            if (selected == null)
                return;

            Animator animator =
                selected.GetComponentInChildren<
                    Animator
                >();

            if (animator == null)
            {
                Debug.LogWarning(
                    "[PolyOne Animator] Aucun Animator trouvé sous l'objet sélectionné. " +
                    "Le controller a quand même été créé."
                );

                return;
            }

            Undo.RecordObject(
                animator,
                "Assign PolyOne Animator"
            );

            animator.runtimeAnimatorController =
                controller;

            animator.applyRootMotion =
                false;

            EditorUtility.SetDirty(
                animator
            );


            PlayerAnimationBridge bridge =
                selected.GetComponentInChildren<
                    PlayerAnimationBridge
                >();

            if (bridge == null)
            {
                bridge =
                    selected.GetComponent<
                        PlayerAnimationBridge
                    >();
            }

            if (bridge == null)
                return;

            SerializedObject serializedBridge =
                new SerializedObject(
                    bridge
                );

            SerializedProperty animatorProperty =
                serializedBridge.FindProperty(
                    "animator"
                );

            if (animatorProperty != null)
            {
                animatorProperty.objectReferenceValue =
                    animator;

                serializedBridge
                    .ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(
                    bridge
                );
            }
        }


        // ============================================================
        // FOLDERS
        // ============================================================

        private static void EnsureOutputFolder()
        {
            if (!AssetDatabase.IsValidFolder(
                    "Assets/Animations"))
            {
                AssetDatabase.CreateFolder(
                    "Assets",
                    "Animations"
                );
            }

            if (!AssetDatabase.IsValidFolder(
                    OutputFolder))
            {
                AssetDatabase.CreateFolder(
                    "Assets/Animations",
                    "Player"
                );
            }
        }
    }
}

#endif
