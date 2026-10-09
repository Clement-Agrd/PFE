#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using KevinIglesias;

namespace ProfessionalTPS.EditorTools
{
    /// <summary>
    /// Construit un Animator Controller joueur basé sur
    /// Kevin Iglesias - Human Animations (Male).
    ///
    /// Base Layer :
    /// - locomotion
    /// - strafe directionnel
    /// - saut
    /// - roll temporaire
    ///
    /// Upper Body Melee :
    /// - combat 1H avec torse + bras
    ///
    /// Upper Body Bow :
    /// - arc Load / Hold / Release
    ///
    /// Les layers utilisent le masque officiel Kevin Iglesias
    /// "Human Body Upper Mask". Ce masque contient notamment
    /// Rig/B-root/B-spineProxy, nécessaire pour conserver
    /// l'orientation correcte du torse avec les animations d'arc.
    ///
    /// Le builder configure aussi automatiquement SpineProxy
    /// sur le personnage sélectionné lorsque le rig Kevin est présent.
    /// </summary>
    public static class KevinIglesiasPlayerAnimatorBuilder
    {
        private const string OutputFolder =
            "Assets/Animations/Player";

        private const string ControllerPath =
            OutputFolder +
            "/PFE_KevinIglesias_Player.controller";

        private const string KevinUpperBodyMaskPath =
            "Assets/Animations/Kevin Iglesias/Human Animations/Models/" +
            "Avatar Masks/Human Body Upper Mask.mask";

        private const string LegacyMeleeMaskPath =
            OutputFolder +
            "/PFE_UpperBody_Melee.mask";

        private const string LegacyBowMaskPath =
            OutputFolder +
            "/PFE_UpperBody_Bow.mask";

        private const string LegacyMaskPath =
            OutputFolder +
            "/PFE_UpperBody.mask";

        private const string MaleRoot =
            "Assets/Animations/Kevin Iglesias/Human Animations/Animations/Male";

        private const string PolyOneTrippingPath =
            "Assets/PolyOne/Basic Motions/Animation/Tripping.anim";


        // ============================================================
        // MENU
        // ============================================================

        [MenuItem(
            "Tools/PFE/Player/Build Kevin Iglesias Animator"
        )]
        public static void Build()
        {
            EnsureOutputFolder();

            Dictionary<string, AnimationClip> clips =
                LoadClips();

            if (clips == null)
                return;

            AnimatorController controller =
                CreateController();

            DeleteGeneratedMasks();

            AvatarMask upperBodyMask =
                LoadKevinUpperBodyMask();

            if (upperBodyMask == null)
                return;

            AddParameters(
                controller
            );

            BuildBaseLayer(
                controller,
                clips
            );

            BuildMeleeUpperBodyLayer(
                controller,
                upperBodyMask,
                clips
            );

            BuildBowUpperBodyLayer(
                controller,
                upperBodyMask,
                clips
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
                "[Kevin Iglesias Animator] Controller créé : " +
                ControllerPath +
                " | Kevin upper-body mask : " +
                KevinUpperBodyMaskPath +
                " | SpineProxy configuré automatiquement si disponible"
            );
        }


        // ============================================================
        // CONTROLLER
        // ============================================================

        private static AnimatorController CreateController()
        {
            if (AssetDatabase.LoadAssetAtPath<
                    AnimatorController
                >(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(
                    ControllerPath
                );
            }

            return AnimatorController
                .CreateAnimatorControllerAtPath(
                    ControllerPath
                );
        }


        // ============================================================
        // BASE LAYER
        // ============================================================

        private static void BuildBaseLayer(
            AnimatorController controller,
            Dictionary<string, AnimationClip> clips)
        {
            AnimatorStateMachine machine =
                controller.layers[0]
                    .stateMachine;

            machine.name =
                "Base Locomotion";


            // --------------------------------------------------------
            // FREE LOCOMOTION
            // --------------------------------------------------------

            BlendTree freeLocomotion =
                new BlendTree
                {
                    name =
                        "Free Locomotion",
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
                clips["WalkForward"],
                0.25f
            );

            freeLocomotion.AddChild(
                clips["RunForward"],
                0.65f
            );

            freeLocomotion.AddChild(
                clips["SprintForward"],
                1f
            );

            AnimatorState freeState =
                machine.AddState(
                    "Free Locomotion",
                    new Vector3(
                        260f,
                        80f,
                        0f
                    )
                );

            freeState.motion =
                freeLocomotion;

            machine.defaultState =
                freeState;


            // --------------------------------------------------------
            // DIRECTIONAL LOCOMOTION
            // --------------------------------------------------------

            BlendTree directional =
                new BlendTree
                {
                    name =
                        "Directional Locomotion",
                    blendType =
                        BlendTreeType.FreeformDirectional2D,
                    blendParameter =
                        "MoveX",
                    blendParameterY =
                        "MoveY"
                };

            AssetDatabase.AddObjectToAsset(
                directional,
                controller
            );

            directional.AddChild(
                clips["Idle"],
                Vector2.zero
            );

            directional.AddChild(
                clips["RunForward"],
                new Vector2(
                    0f,
                    1f
                )
            );

            directional.AddChild(
                clips["StrafeForwardRight"],
                new Vector2(
                    0.7f,
                    0.7f
                )
            );

            directional.AddChild(
                clips["StrafeRight"],
                new Vector2(
                    1f,
                    0f
                )
            );

            directional.AddChild(
                clips["StrafeBackwardRight"],
                new Vector2(
                    0.7f,
                    -0.7f
                )
            );

            directional.AddChild(
                clips["RunBackward"],
                new Vector2(
                    0f,
                    -1f
                )
            );

            directional.AddChild(
                clips["StrafeBackwardLeft"],
                new Vector2(
                    -0.7f,
                    -0.7f
                )
            );

            directional.AddChild(
                clips["StrafeLeft"],
                new Vector2(
                    -1f,
                    0f
                )
            );

            directional.AddChild(
                clips["StrafeForwardLeft"],
                new Vector2(
                    -0.7f,
                    0.7f
                )
            );

            AnimatorState directionalState =
                machine.AddState(
                    "Directional Locomotion",
                    new Vector3(
                        550f,
                        80f,
                        0f
                    )
                );

            directionalState.motion =
                directional;

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

            AnimatorState jumpBegin =
                machine.AddState(
                    "Jump Begin",
                    new Vector3(
                        340f,
                        -100f,
                        0f
                    )
                );

            jumpBegin.motion =
                clips["JumpBegin"];

            AnimatorState fall =
                machine.AddState(
                    "Fall",
                    new Vector3(
                        520f,
                        -100f,
                        0f
                    )
                );

            fall.motion =
                clips["Fall"];

            AnimatorState land =
                machine.AddState(
                    "Land",
                    new Vector3(
                        700f,
                        -100f,
                        0f
                    )
                );

            land.motion =
                clips["JumpLand"];


            AnimatorStateTransition jumpTransition =
                machine.AddAnyStateTransition(
                    jumpBegin
                );

            ConfigureImmediateTransition(
                jumpTransition,
                0.04f
            );

            jumpTransition.canTransitionToSelf =
                false;

            jumpTransition.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "Jump"
            );


            AnimatorStateTransition toFall =
                jumpBegin.AddTransition(
                    fall
                );

            ConfigureImmediateTransition(
                toFall,
                0.08f
            );

            toFall.AddCondition(
                AnimatorConditionMode.Less,
                0.01f,
                "VerticalSpeed"
            );


            AnimatorStateTransition toLand =
                fall.AddTransition(
                    land
                );

            ConfigureImmediateTransition(
                toLand,
                0.05f
            );

            toLand.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "Grounded"
            );


            AddExitReturnTransition(
                land,
                freeState,
                false,
                0.85f
            );

            AddExitReturnTransition(
                land,
                directionalState,
                true,
                0.85f
            );


            // --------------------------------------------------------
            // ROLL TEMP
            // --------------------------------------------------------

            if (clips.TryGetValue(
                    "RollTemp",
                    out AnimationClip rollClip) &&
                rollClip != null)
            {
                AnimatorState roll =
                    machine.AddState(
                        "Roll (Temp PolyOne)",
                        new Vector3(
                            820f,
                            -220f,
                            0f
                        )
                    );

                roll.motion =
                    rollClip;

                AnimatorStateTransition rollTransition =
                    machine.AddAnyStateTransition(
                        roll
                    );

                ConfigureImmediateTransition(
                    rollTransition,
                    0.03f
                );

                rollTransition.canTransitionToSelf =
                    false;

                rollTransition.AddCondition(
                    AnimatorConditionMode.If,
                    0f,
                    "Roll"
                );

                AddExitReturnTransition(
                    roll,
                    freeState,
                    false,
                    0.9f
                );

                AddExitReturnTransition(
                    roll,
                    directionalState,
                    true,
                    0.9f
                );
            }
        }


        // ============================================================
        // UPPER BODY - MELEE
        // ============================================================

        private static void BuildMeleeUpperBodyLayer(
            AnimatorController controller,
            AvatarMask mask,
            Dictionary<string, AnimationClip> clips)
        {
            AnimatorStateMachine machine =
                new AnimatorStateMachine
                {
                    name =
                        "Upper Body Melee"
                };

            AssetDatabase.AddObjectToAsset(
                machine,
                controller
            );

            AnimatorControllerLayer layer =
                new AnimatorControllerLayer
                {
                    name =
                        "Upper Body Melee",
                    defaultWeight =
                        1f,
                    avatarMask =
                        mask,
                    blendingMode =
                        AnimatorLayerBlendingMode.Override,
                    stateMachine =
                        machine
                };

            controller.AddLayer(
                layer
            );

            AnimatorState empty =
                machine.AddState(
                    "Empty",
                    new Vector3(
                        120f,
                        100f,
                        0f
                    )
                );

            machine.defaultState =
                empty;

            AnimatorState meleeIdle =
                machine.AddState(
                    "1H Combat Idle",
                    new Vector3(
                        350f,
                        40f,
                        0f
                    )
                );

            meleeIdle.motion =
                clips["MeleeIdle"];

            AnimatorState meleeR =
                machine.AddState(
                    "1H Attack R",
                    new Vector3(
                        570f,
                        0f,
                        0f
                    )
                );

            meleeR.motion =
                clips["MeleeR"];

            ConfigureAttackSpeed(
                meleeR
            );

            AnimatorState meleeL =
                machine.AddState(
                    "1H Attack L",
                    new Vector3(
                        570f,
                        100f,
                        0f
                    )
                );

            meleeL.motion =
                clips["MeleeL"];

            ConfigureAttackSpeed(
                meleeL
            );

            AddModeAimEnter(
                empty,
                meleeIdle,
                0
            );

            AddAimExit(
                meleeIdle,
                empty
            );

            AddModeExit(
                meleeIdle,
                empty,
                0
            );

            AddAnyTrigger(
                machine,
                meleeR,
                "Melee1",
                0.03f
            );

            AddAnyTrigger(
                machine,
                meleeL,
                "Melee2",
                0.03f
            );

            AddAnyTrigger(
                machine,
                meleeR,
                "Melee3",
                0.03f
            );

            AddCombatAttackReturns(
                meleeR,
                meleeIdle,
                empty,
                0
            );

            AddCombatAttackReturns(
                meleeL,
                meleeIdle,
                empty,
                0
            );
        }


        // ============================================================
        // UPPER BODY - BOW
        // ============================================================

        private static void BuildBowUpperBodyLayer(
            AnimatorController controller,
            AvatarMask mask,
            Dictionary<string, AnimationClip> clips)
        {
            AnimatorStateMachine machine =
                new AnimatorStateMachine
                {
                    name =
                        "Upper Body Bow"
                };

            AssetDatabase.AddObjectToAsset(
                machine,
                controller
            );

            AnimatorControllerLayer layer =
                new AnimatorControllerLayer
                {
                    name =
                        "Upper Body Bow",
                    defaultWeight =
                        1f,
                    avatarMask =
                        mask,
                    blendingMode =
                        AnimatorLayerBlendingMode.Override,
                    stateMachine =
                        machine
                };

            controller.AddLayer(
                layer
            );

            AnimatorState empty =
                machine.AddState(
                    "Empty",
                    new Vector3(
                        120f,
                        100f,
                        0f
                    )
                );

            machine.defaultState =
                empty;

            AnimatorState bowIdle =
                machine.AddState(
                    "Bow Idle",
                    new Vector3(
                        350f,
                        100f,
                        0f
                    )
                );

            bowIdle.motion =
                clips["BowIdle"];

            AnimatorState bowLoad =
                machine.AddState(
                    "Bow Load",
                    new Vector3(
                        570f,
                        60f,
                        0f
                    )
                );

            bowLoad.motion =
                clips["BowLoad"];

            ConfigureAttackSpeed(
                bowLoad
            );

            AnimatorState bowHold =
                machine.AddState(
                    "Bow Hold",
                    new Vector3(
                        760f,
                        60f,
                        0f
                    )
                );

            bowHold.motion =
                clips["BowHold"];

            AnimatorState bowRelease =
                machine.AddState(
                    "Bow Release",
                    new Vector3(
                        950f,
                        60f,
                        0f
                    )
                );

            bowRelease.motion =
                clips["BowRelease"];

            ConfigureAttackSpeed(
                bowRelease
            );

            AddModeAimEnter(
                empty,
                bowIdle,
                1
            );

            AddAimExit(
                bowIdle,
                empty
            );

            AddModeExit(
                bowIdle,
                empty,
                1
            );

            AddBowDrawingTransition(
                empty,
                bowLoad
            );

            AddBowDrawingTransition(
                bowIdle,
                bowLoad
            );

            AnimatorStateTransition loadToHold =
                bowLoad.AddTransition(
                    bowHold
                );

            loadToHold.hasExitTime =
                true;

            loadToHold.exitTime =
                0.88f;

            loadToHold.hasFixedDuration =
                true;

            loadToHold.duration =
                0.05f;

            loadToHold.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "BowDrawing"
            );

            AddAnyTrigger(
                machine,
                bowRelease,
                "BowAttack",
                0.02f
            );

            AnimatorStateTransition releaseToBowIdle =
                bowRelease.AddTransition(
                    bowIdle
                );

            releaseToBowIdle.hasExitTime =
                true;

            releaseToBowIdle.exitTime =
                0.92f;

            releaseToBowIdle.duration =
                0.06f;

            releaseToBowIdle.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "Aiming"
            );

            releaseToBowIdle.AddCondition(
                AnimatorConditionMode.Equals,
                1f,
                "CombatMode"
            );

            AnimatorStateTransition releaseToEmpty =
                bowRelease.AddTransition(
                    empty
                );

            releaseToEmpty.hasExitTime =
                true;

            releaseToEmpty.exitTime =
                0.92f;

            releaseToEmpty.duration =
                0.06f;

            releaseToEmpty.AddCondition(
                AnimatorConditionMode.IfNot,
                0f,
                "Aiming"
            );

            AddModeExit(
                bowRelease,
                empty,
                1
            );

            AddModeExit(
                bowHold,
                empty,
                1
            );

            AnimatorStateTransition cancelHold =
                bowHold.AddTransition(
                    empty
                );

            ConfigureImmediateTransition(
                cancelHold,
                0.06f
            );

            cancelHold.AddCondition(
                AnimatorConditionMode.IfNot,
                0f,
                "BowDrawing"
            );
        }


        // ============================================================
        // MASK
        // ============================================================

        private static AvatarMask LoadKevinUpperBodyMask()
        {
            AvatarMask mask =
                AssetDatabase.LoadAssetAtPath<
                    AvatarMask
                >(KevinUpperBodyMaskPath);

            if (mask == null)
            {
                Debug.LogError(
                    "[Kevin Iglesias Animator] Masque officiel introuvable : " +
                    KevinUpperBodyMaskPath
                );

                return null;
            }

            return mask;
        }


        private static void DeleteGeneratedMasks()
        {
            DeleteMaskIfPresent(
                LegacyMaskPath
            );

            DeleteMaskIfPresent(
                LegacyMeleeMaskPath
            );

            DeleteMaskIfPresent(
                LegacyBowMaskPath
            );
        }


        private static void DeleteMaskIfPresent(
            string path)
        {
            if (AssetDatabase.LoadAssetAtPath<
                    AvatarMask
                >(path) == null)
            {
                return;
            }

            AssetDatabase.DeleteAsset(
                path
            );
        }


        // ============================================================
        // PARAMETERS
        // ============================================================

        private static void AddParameters(
            AnimatorController controller)
        {
            AddFloat(controller, "Speed", 0f);
            AddFloat(controller, "MoveX", 0f);
            AddFloat(controller, "MoveY", 0f);
            AddFloat(controller, "VerticalSpeed", 0f);
            AddBool(controller, "Grounded", true);
            AddBool(controller, "Aiming", false);
            AddBool(controller, "Sprinting", false);
            AddBool(controller, "DirectionalLocomotion", false);
            AddInt(controller, "CombatMode", 0);
            AddFloat(controller, "AttackSpeed", 1f);
            AddBool(controller, "BowDrawing", false);
            AddFloat(controller, "BowCharge", 0f);

            AddTrigger(controller, "Jump");
            AddTrigger(controller, "Roll");
            AddTrigger(controller, "Melee1");
            AddTrigger(controller, "Melee2");
            AddTrigger(controller, "Melee3");
            AddTrigger(controller, "BowAttack");
            AddTrigger(controller, "MagicAttack");
            AddTrigger(controller, "AxeAttack");
            AddTrigger(controller, "PickaxeAttack");
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
        // TRANSITION HELPERS
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


        private static void AddModeAimEnter(
            AnimatorState from,
            AnimatorState to,
            int mode)
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
                "Aiming"
            );

            transition.AddCondition(
                AnimatorConditionMode.Equals,
                mode,
                "CombatMode"
            );
        }


        private static void AddAimExit(
            AnimatorState from,
            AnimatorState to)
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
                AnimatorConditionMode.IfNot,
                0f,
                "Aiming"
            );
        }


        private static void AddModeExit(
            AnimatorState from,
            AnimatorState to,
            int expectedMode)
        {
            AnimatorStateTransition transition =
                from.AddTransition(
                    to
                );

            ConfigureImmediateTransition(
                transition,
                0.05f
            );

            transition.AddCondition(
                AnimatorConditionMode.NotEqual,
                expectedMode,
                "CombatMode"
            );
        }


        private static void AddAnyTrigger(
            AnimatorStateMachine machine,
            AnimatorState target,
            string trigger,
            float duration)
        {
            AnimatorStateTransition transition =
                machine.AddAnyStateTransition(
                    target
                );

            ConfigureImmediateTransition(
                transition,
                duration
            );

            transition.canTransitionToSelf =
                false;

            transition.AddCondition(
                AnimatorConditionMode.If,
                0f,
                trigger
            );
        }


        private static void AddCombatAttackReturns(
            AnimatorState attack,
            AnimatorState combatIdle,
            AnimatorState empty,
            int mode)
        {
            AnimatorStateTransition toIdle =
                attack.AddTransition(
                    combatIdle
                );

            toIdle.hasExitTime =
                true;

            toIdle.exitTime =
                0.9f;

            toIdle.duration =
                0.06f;

            toIdle.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "Aiming"
            );

            toIdle.AddCondition(
                AnimatorConditionMode.Equals,
                mode,
                "CombatMode"
            );


            AnimatorStateTransition toEmpty =
                attack.AddTransition(
                    empty
                );

            toEmpty.hasExitTime =
                true;

            toEmpty.exitTime =
                0.9f;

            toEmpty.duration =
                0.06f;

            toEmpty.AddCondition(
                AnimatorConditionMode.IfNot,
                0f,
                "Aiming"
            );
        }


        private static void AddBowDrawingTransition(
            AnimatorState from,
            AnimatorState to)
        {
            AnimatorStateTransition transition =
                from.AddTransition(
                    to
                );

            ConfigureImmediateTransition(
                transition,
                0.04f
            );

            transition.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "BowDrawing"
            );

            transition.AddCondition(
                AnimatorConditionMode.Equals,
                1f,
                "CombatMode"
            );
        }


        private static void AddExitReturnTransition(
            AnimatorState from,
            AnimatorState to,
            bool directional,
            float exitTime)
        {
            AnimatorStateTransition transition =
                from.AddTransition(
                    to
                );

            transition.hasExitTime =
                true;

            transition.exitTime =
                exitTime;

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


        private static void ConfigureAttackSpeed(
            AnimatorState state)
        {
            state.speedParameter =
                "AttackSpeed";

            state.speedParameterActive =
                true;
        }


        // ============================================================
        // CLIPS
        // ============================================================

        private static Dictionary<
            string,
            AnimationClip
        > LoadClips()
        {
            Dictionary<
                string,
                string
            > paths =
                new Dictionary<
                    string,
                    string
                >
                {
                    {
                        "Idle",
                        MaleRoot +
                        "/Idles/HumanM@Idle01.fbx"
                    },
                    {
                        "WalkForward",
                        MaleRoot +
                        "/Movement/Walk/HumanM@Walk01_Forward.fbx"
                    },
                    {
                        "RunForward",
                        MaleRoot +
                        "/Movement/Run/HumanM@Run01_Forward.fbx"
                    },
                    {
                        "RunBackward",
                        MaleRoot +
                        "/Movement/Run/HumanM@Run01_Backward.fbx"
                    },
                    {
                        "SprintForward",
                        MaleRoot +
                        "/Movement/Sprint/HumanM@Sprint01_Forward.fbx"
                    },
                    {
                        "StrafeForwardLeft",
                        MaleRoot +
                        "/Movement/Strafe/StrafeRun/HumanM@StrafeRun01_ForwardLeft.fbx"
                    },
                    {
                        "StrafeForwardRight",
                        MaleRoot +
                        "/Movement/Strafe/StrafeRun/HumanM@StrafeRun01_ForwardRight.fbx"
                    },
                    {
                        "StrafeLeft",
                        MaleRoot +
                        "/Movement/Strafe/StrafeRun/HumanM@StrafeRun01_Left.fbx"
                    },
                    {
                        "StrafeRight",
                        MaleRoot +
                        "/Movement/Strafe/StrafeRun/HumanM@StrafeRun01_Right.fbx"
                    },
                    {
                        "StrafeBackwardLeft",
                        MaleRoot +
                        "/Movement/Strafe/StrafeRun/HumanM@StrafeRun01_BackwardLeft.fbx"
                    },
                    {
                        "StrafeBackwardRight",
                        MaleRoot +
                        "/Movement/Strafe/StrafeRun/HumanM@StrafeRun01_BackwardRight.fbx"
                    },
                    {
                        "JumpBegin",
                        MaleRoot +
                        "/Movement/Jump/HumanM@Jump01 - Begin.fbx"
                    },
                    {
                        "Fall",
                        MaleRoot +
                        "/Movement/Jump/HumanM@Fall01.fbx"
                    },
                    {
                        "JumpLand",
                        MaleRoot +
                        "/Movement/Jump/HumanM@Jump01 - Land.fbx"
                    },
                    {
                        "MeleeIdle",
                        MaleRoot +
                        "/Combat/1H/HumanM@CombatIdle1H01.fbx"
                    },
                    {
                        "MeleeL",
                        MaleRoot +
                        "/Combat/1H/HumanM@Attack1H01_L.fbx"
                    },
                    {
                        "MeleeR",
                        MaleRoot +
                        "/Combat/1H/HumanM@Attack1H01_R.fbx"
                    },
                    {
                        "BowIdle",
                        MaleRoot +
                        "/Combat/Bow/HumanM@BowIdle01.fbx"
                    },
                    {
                        "BowLoad",
                        MaleRoot +
                        "/Combat/Bow/HumanM@BowShot01 - Load.fbx"
                    },
                    {
                        "BowHold",
                        MaleRoot +
                        "/Combat/Bow/HumanM@BowShot01 - Hold.fbx"
                    },
                    {
                        "BowRelease",
                        MaleRoot +
                        "/Combat/Bow/HumanM@BowShot01 - Release.fbx"
                    }
                };

            Dictionary<
                string,
                AnimationClip
            > clips =
                new Dictionary<
                    string,
                    AnimationClip
                >();

            foreach (
                KeyValuePair<string, string>
                pair in paths)
            {
                AnimationClip clip =
                    LoadClipFromAsset(
                        pair.Value
                    );

                if (clip == null)
                {
                    Debug.LogError(
                        "[Kevin Iglesias Animator] Clip introuvable : " +
                        pair.Value
                    );

                    return null;
                }

                clips[pair.Key] =
                    clip;
            }


            AnimationClip roll =
                AssetDatabase.LoadAssetAtPath<
                    AnimationClip
                >(
                    PolyOneTrippingPath
                );

            if (roll != null)
            {
                clips["RollTemp"] =
                    roll;
            }
            else
            {
                Debug.LogWarning(
                    "[Kevin Iglesias Animator] Aucune animation de roll Kevin Iglesias trouvée. " +
                    "Le fallback PolyOne Tripping n'est pas disponible non plus : " +
                    "le gameplay de roll fonctionnera sans animation."
                );
            }

            return clips;
        }


        private static AnimationClip LoadClipFromAsset(
            string path)
        {
            Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(
                    path
                );

            foreach (Object asset in assets)
            {
                if (asset is not AnimationClip clip)
                    continue;

                if (clip.name.StartsWith(
                        "__preview__"))
                {
                    continue;
                }

                return clip;
            }

            return null;
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
                    "[Kevin Iglesias Animator] Aucun Animator trouvé sous l'objet sélectionné. " +
                    "Le controller a quand même été créé."
                );

                return;
            }

            Undo.RecordObject(
                animator,
                "Assign Kevin Iglesias Animator"
            );

            animator.runtimeAnimatorController =
                controller;

            animator.applyRootMotion =
                false;

            EditorUtility.SetDirty(
                animator
            );

            ConfigureSpineProxy(
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
        // KEVIN IGLESIAS SPINE PROXY
        // ============================================================

        private static void ConfigureSpineProxy(
            Animator animator)
        {
            if (animator == null)
                return;

            Transform proxy =
                FindDeepChild(
                    animator.transform,
                    "B-spineProxy"
                );

            Transform spine =
                FindDeepChild(
                    animator.transform,
                    "B-spine"
                );

            if (proxy == null ||
                spine == null)
            {
                Debug.LogWarning(
                    "[Kevin Iglesias Animator] SpineProxy non configuré : " +
                    "B-spineProxy ou B-spine est introuvable sous l'Animator. " +
                    "Sur un rig custom, recrée la structure prévue par Kevin Iglesias " +
                    "et assigne manuellement le SpineProxy."
                );

                return;
            }

            SpineProxy spineProxy =
                proxy.GetComponent<
                    SpineProxy
                >();

            if (spineProxy == null)
            {
                spineProxy =
                    Undo.AddComponent<
                        SpineProxy
                    >(
                        proxy.gameObject
                    );
            }

            SerializedObject serializedProxy =
                new SerializedObject(
                    spineProxy
                );

            SerializedProperty originalSpine =
                serializedProxy.FindProperty(
                    "originalSpine"
                );

            if (originalSpine == null)
            {
                Debug.LogWarning(
                    "[Kevin Iglesias Animator] Le champ originalSpine " +
                    "du composant SpineProxy est introuvable."
                );

                return;
            }

            originalSpine.objectReferenceValue =
                spine;

            serializedProxy
                .ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(
                spineProxy
            );

            if (PrefabUtility.IsPartOfPrefabInstance(
                    spineProxy))
            {
                PrefabUtility
                    .RecordPrefabInstancePropertyModifications(
                        spineProxy
                    );
            }

            Debug.Log(
                "[Kevin Iglesias Animator] SpineProxy configuré : " +
                proxy.name +
                " -> " +
                spine.name
            );
        }


        private static Transform FindDeepChild(
            Transform root,
            string childName)
        {
            if (root == null)
                return null;

            if (root.name == childName)
                return root;

            for (int i = 0;
                 i < root.childCount;
                 i++)
            {
                Transform result =
                    FindDeepChild(
                        root.GetChild(i),
                        childName
                    );

                if (result != null)
                    return result;
            }

            return null;
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
