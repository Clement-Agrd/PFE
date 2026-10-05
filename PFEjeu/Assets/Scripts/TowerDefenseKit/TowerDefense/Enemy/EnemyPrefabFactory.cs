#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

using Core.HealthSystem;
using Core.PoolingSystem;
using Core.StatsSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Outil de test :
    /// Tools > PFE > Enemies > Create Modular Test Enemies
    ///
    /// Génère deux prefabs simples :
    /// Enemy_Classic et Enemy_Combo.
    /// </summary>
    public static class EnemyPrefabFactory
    {
        private const string ClassicPath =
            "Assets/Prefabs/Enemy_Classic.prefab";

        private const string ComboPath =
            "Assets/Prefabs/Enemy_Combo.prefab";

        [MenuItem(
            "Tools/PFE/Enemies/Create Modular Test Enemies"
        )]
        public static void CreateTestEnemies()
        {
            DamageType physical =
                FindPhysicalDamageType();

            CreateEnemyPrefab(
                ClassicPath,
                "Enemy_Classic",
                false,
                physical
            );

            CreateEnemyPrefab(
                ComboPath,
                "Enemy_Combo",
                true,
                physical
            );

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject =
                AssetDatabase.LoadAssetAtPath<
                    GameObject
                >(
                    ClassicPath
                );

            Debug.Log(
                "[EnemyPrefabFactory] Enemy_Classic et Enemy_Combo créés/mis à jour dans Assets/Prefabs."
            );
        }

        private static void CreateEnemyPrefab(
            string path,
            string objectName,
            bool combo,
            DamageType physical)
        {
            GameObject root =
                GameObject.CreatePrimitive(
                    PrimitiveType.Capsule
                );

            root.name =
                objectName;

            if (combo)
            {
                root.transform.localScale =
                    Vector3.one *
                    1.15f;
            }

            NavMeshAgent agent =
                root.AddComponent<
                    NavMeshAgent
                >();

            agent.radius = 0.4f;
            agent.height = 2f;
            agent.angularSpeed = 720f;
            agent.acceleration = 20f;

            EntityStats stats =
                root.AddComponent<
                    EntityStats
                >();

            SetStat(
                stats,
                EnumStats.StatTypes.Life,
                combo ? 130f : 80f
            );

            SetStat(
                stats,
                EnumStats.StatTypes.PhysicDamage,
                combo ? 13f : 10f
            );

            SetStat(
                stats,
                EnumStats.StatTypes.DefensePhysic,
                combo ? 10f : 0f
            );

            SetStat(
                stats,
                EnumStats.StatTypes.Speed,
                combo ? 3.2f : 3.8f
            );

            SetStat(
                stats,
                EnumStats.StatTypes.AttackSpeed,
                1f
            );

            root.AddComponent<Health>();
            root.AddComponent<PooledObject>();

            EnemyTargetSensor sensor =
                root.AddComponent<
                    EnemyTargetSensor
                >();

            ConfigureTargetMask(
                sensor
            );

            root.AddComponent<
                EnemyPathFollower
            >();

            EnemyAttackModule attack;

            if (combo)
            {
                EnemyComboMeleeAttack comboAttack =
                    root.AddComponent<
                        EnemyComboMeleeAttack
                    >();

                attack =
                    comboAttack;

                SetFloat(
                    comboAttack,
                    "attackRange",
                    1.9f
                );

                SetFloat(
                    comboAttack,
                    "openingLightChance",
                    0.8f
                );

                SetFloat(
                    comboAttack,
                    "chainHeavyChance",
                    0.85f
                );

                SetFloat(
                    comboAttack,
                    "lightBaseDamage",
                    3f
                );

                SetFloat(
                    comboAttack,
                    "heavyBaseDamage",
                    12f
                );
            }
            else
            {
                EnemyMeleeAttack melee =
                    root.AddComponent<
                        EnemyMeleeAttack
                    >();

                attack =
                    melee;

                SetFloat(
                    melee,
                    "attackRange",
                    1.7f
                );

                SetFloat(
                    melee,
                    "baseDamage",
                    5f
                );

                SetFloat(
                    melee,
                    "attackInterval",
                    1.25f
                );
            }

            if (physical != null)
            {
                SetObjectReference(
                    attack,
                    "damageType",
                    physical
                );
            }

            EnemyController controller =
                root.AddComponent<
                    EnemyController
                >();

            SerializedObject controllerSO =
                new SerializedObject(
                    controller
                );

            SerializedProperty attackProperty =
                controllerSO.FindProperty(
                    "attackModule"
                );

            if (attackProperty != null)
            {
                attackProperty.objectReferenceValue =
                    attack;
            }

            controllerSO.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(
                root,
                path
            );

            Object.DestroyImmediate(
                root
            );
        }

        private static void ConfigureTargetMask(
            EnemyTargetSensor sensor)
        {
            int mask = 0;

            int playerLayer =
                LayerMask.NameToLayer(
                    "Player"
                );

            int soldierLayer =
                LayerMask.NameToLayer(
                    "Soldier"
                );

            if (playerLayer >= 0)
                mask |= 1 << playerLayer;

            if (soldierLayer >= 0)
                mask |= 1 << soldierLayer;

            SerializedObject serialized =
                new SerializedObject(
                    sensor
                );

            SerializedProperty property =
                serialized.FindProperty(
                    "targetMask"
                );

            if (property != null)
                property.intValue = mask;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetStat(
            EntityStats stats,
            EnumStats.StatTypes type,
            float value)
        {
            SerializedObject serialized =
                new SerializedObject(
                    stats
                );

            SerializedProperty list =
                serialized.FindProperty(
                    "baseStats"
                );

            if (list == null)
                return;

            for (int i = 0;
                 i < list.arraySize;
                 i++)
            {
                SerializedProperty element =
                    list.GetArrayElementAtIndex(
                        i
                    );

                SerializedProperty typeProperty =
                    element.FindPropertyRelative(
                        "type"
                    );

                SerializedProperty valueProperty =
                    element.FindPropertyRelative(
                        "baseValue"
                    );

                if (typeProperty == null ||
                    valueProperty == null)
                {
                    continue;
                }

                if (typeProperty.enumValueIndex !=
                    (int)type)
                {
                    continue;
                }

                valueProperty.floatValue =
                    value;

                break;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(
            Object target,
            string propertyName,
            float value)
        {
            SerializedObject serialized =
                new SerializedObject(
                    target
                );

            SerializedProperty property =
                serialized.FindProperty(
                    propertyName
                );

            if (property != null)
                property.floatValue = value;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectReference(
            Object target,
            string propertyName,
            Object value)
        {
            SerializedObject serialized =
                new SerializedObject(
                    target
                );

            SerializedProperty property =
                serialized.FindProperty(
                    propertyName
                );

            if (property != null)
                property.objectReferenceValue = value;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static DamageType FindPhysicalDamageType()
        {
            string[] guids =
                AssetDatabase.FindAssets(
                    "t:DamageType"
                );

            for (int i = 0;
                 i < guids.Length;
                 i++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(
                        guids[i]
                    );

                DamageType damageType =
                    AssetDatabase.LoadAssetAtPath<
                        DamageType
                    >(
                        path
                    );

                if (damageType != null &&
                    damageType.Category ==
                        DamageCategory.Physical)
                {
                    return damageType;
                }
            }

            Debug.LogWarning(
                "[EnemyPrefabFactory] Aucun DamageType Physical trouvé. Les prefabs seront créés sans DamageType assigné."
            );

            return null;
        }
    }
}

#endif
