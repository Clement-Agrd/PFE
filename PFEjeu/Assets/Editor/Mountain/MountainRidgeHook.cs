using System;
using Defense.Relief.EditorTools;
using UnityEditor;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Branche les cretes lointaines sur l'outil de sculpt du relief (qui reste l'unique ecrivain de la heightmap).
    /// Actif seulement si MountainRidgeProfile.Enabled : sinon le sculpt est strictement celui d'origine.
    /// </summary>
    [InitializeOnLoad]
    internal static class MountainRidgeHook
    {
        static MountainRidgeHook()
        {
            DefenseReliefSculptTool.OuterModifierFactory = CreateModifier;
            DefenseReliefSculptTool.HeightmapPostProcessor = PostProcess;
        }

        private static void PostProcess(float[,] heights, UnityEngine.Vector3 origin, UnityEngine.Vector3 size)
        {
            MountainRidgeProfile profile = MountainRidgeProfile.LoadOrCreate();
            if (!profile.Enabled) return;
            MountainCragPass.Apply(heights, origin, size, profile, MountainProfile.LoadOrCreate().ValleyY);
        }

        private static Func<float, float, float, float> CreateModifier()
        {
            MountainRidgeProfile profile = MountainRidgeProfile.LoadOrCreate();
            if (!profile.Enabled) return null;

            UnityEngine.Terrain terrain = DefenseReliefScene.Terrain;
            var field = new MountainRidgeField(
                profile,
                DefenseReliefScene.HersePosition,
                terrain.transform.position,
                terrain.terrainData.size,
                MountainProfile.LoadOrCreate().ValleyY);
            return field.Apply;
        }
    }
}
