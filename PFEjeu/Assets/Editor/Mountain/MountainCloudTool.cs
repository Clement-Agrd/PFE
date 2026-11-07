using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Cree ou recree les racines de nuages lointains : "Environment_Clouds" (vallees, cretes, mer de nuages) et
    /// "Test_Atmosphere" (voiles en altitude, brume d'eperons : tout ce qui est experimental porte le prefixe Test).
    /// Idempotent et annulable. N'ajoute aucune lumiere et ne modifie ni VFX_Mist ni le materiau de brume.
    /// </summary>
    public static class MountainCloudTool
    {
        private const string MainRootName = "Environment_Clouds";
        private const string TestRootName = "Test_Atmosphere";
        private const string MaterialPath = "Assets/VFX/Ambiance/M_MistPuff.mat";

        private readonly struct Preset
        {
            public readonly string name;
            public readonly MountainCloudBank.BankKind kind;
            public readonly int seed;
            public readonly int count;
            public readonly Vector2 distance;
            public readonly Vector2 size;
            public readonly Vector2 alpha;
            public readonly Vector2 height;
            public readonly float sorting;
            public readonly float drift;

            public Preset(string name, MountainCloudBank.BankKind kind, int seed, int count, Vector2 distance, Vector2 size,
                Vector2 alpha, Vector2 height, float sorting, float drift)
            {
                this.name = name; this.kind = kind; this.seed = seed; this.count = count; this.distance = distance;
                this.size = size; this.alpha = alpha; this.height = height; this.sorting = sorting; this.drift = drift;
            }
        }

        private static readonly Preset[] MainPresets =
        {
            // Mer de nuages au-dela de la derniere crete : masque le cadre plat du terrain (dessinee en premier, au fond).
            new Preset("Clouds_Sea", MountainCloudBank.BankKind.CloudSea, 9101, 70,
                new Vector2(450f, 540f), new Vector2(190f, 340f), new Vector2(0.24f, 0.38f), new Vector2(2f, 10f), 120f, 1.5f),
            // Bancs de vallee entre les lignes de crete.
            new Preset("Clouds_Valleys", MountainCloudBank.BankKind.ValleyBank, 9102, 60,
                new Vector2(305f, 420f), new Vector2(110f, 240f), new Vector2(0.17f, 0.30f), new Vector2(14f, 30f), 80f, 2.5f),
            // Voiles qui s'accrochent aux cretes.
            new Preset("Clouds_Crests", MountainCloudBank.BankKind.CrestWisp, 9103, 56,
                new Vector2(320f, 470f), new Vector2(55f, 130f), new Vector2(0.10f, 0.18f), new Vector2(-8f, 6f), 40f, 3.5f),
        };

        private static readonly Preset[] TestPresets =
        {
            // Grands voiles legers passant au-dessus des cretes (profondeur dans le ciel).
            new Preset("Test_Clouds_High", MountainCloudBank.BankKind.CrestWisp, 9201, 40,
                new Vector2(280f, 520f), new Vector2(160f, 320f), new Vector2(0.07f, 0.13f), new Vector2(70f, 160f), 20f, 4.5f),
            // Brume basse qui remonte au pied des eperons (en dehors de la zone de jeu).
            new Preset("Test_Clouds_Spurs", MountainCloudBank.BankKind.ValleyBank, 9202, 44,
                new Vector2(250f, 340f), new Vector2(55f, 120f), new Vector2(0.12f, 0.20f), new Vector2(5f, 16f), 60f, 2f),
        };

        [MenuItem("Tools/Defense/Mountain/Nuages - Creer ou recreer")]
        public static void CreateOrRebuild() => Build(MainRootName, MainPresets);

        [MenuItem("Tools/Defense/Mountain/Nuages - Retirer")]
        public static void Remove() => RemoveRoot(MainRootName);

        [MenuItem("Tools/Defense/Mountain/Test - Atmosphere - Creer ou recreer")]
        public static void CreateOrRebuildTest() => Build(TestRootName, TestPresets);

        [MenuItem("Tools/Defense/Mountain/Test - Atmosphere - Retirer")]
        public static void RemoveTest() => RemoveRoot(TestRootName);

        private static void Build(string rootName, Preset[] presets)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Debug.LogError("[Mountain] Materiau de brume introuvable : " + MaterialPath);
                return;
            }

            GameObject herse = GameObject.Find(DefenseReliefScene.HerseName);
            if (herse == null)
            {
                Debug.LogError("[Mountain] Objet introuvable : " + DefenseReliefScene.HerseName);
                return;
            }

            GameObject existing = GameObject.Find(rootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "Mountain : " + rootName);
            float valleyY = MountainProfile.LoadOrCreate().ValleyY;

            int total = 0;
            foreach (Preset preset in presets)
            {
                var child = new GameObject(preset.name, typeof(ParticleSystem));
                child.transform.SetParent(root.transform, false);
                var bank = child.AddComponent<MountainCloudBank>();
                Apply(bank, preset, material, herse.transform, valleyY);
                total += preset.count;
            }

            DefenseReliefScene.MarkSceneDirty();
            Debug.Log("[Mountain] " + rootName + " cree : " + presets.Length + " couches, " + total + " puffs.");
        }

        private static void RemoveRoot(string rootName)
        {
            GameObject existing = GameObject.Find(rootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);
            DefenseReliefScene.MarkSceneDirty();
            Debug.Log("[Mountain] " + rootName + " retire.");
        }

        private static void Apply(MountainCloudBank bank, Preset p, Material material, Transform anchor, float valleyY)
        {
            var so = new SerializedObject(bank);
            so.FindProperty("kind").enumValueIndex = (int)p.kind;
            so.FindProperty("material").objectReferenceValue = material;
            so.FindProperty("anchor").objectReferenceValue = anchor;
            so.FindProperty("seed").intValue = p.seed;
            so.FindProperty("count").intValue = p.count;
            so.FindProperty("distanceRange").vector2Value = p.distance;
            so.FindProperty("sizeRange").vector2Value = p.size;
            so.FindProperty("alphaRange").vector2Value = p.alpha;
            so.FindProperty("heightRange").vector2Value = p.height;
            so.FindProperty("sortingFudge").floatValue = p.sorting;
            so.FindProperty("driftStrength").floatValue = p.drift;
            so.FindProperty("valleyY").floatValue = valleyY;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
