using UnityEngine;

namespace DustVFX
{
    /// <summary>
    /// One kind of ground. A single DustStep prefab is reused for every surface:
    /// the surface only scales/tints what the prefab emits.
    /// </summary>
    [CreateAssetMenu(menuName = "Dust VFX/Surface", fileName = "DustSurface")]
    public sealed class DustSurface : ScriptableObject
    {
        [Header("Identity")]
        public DustSurfaceKind kind = DustSurfaceKind.Earth;

        [Tooltip("No dust at all (water, wet ground, 'none').")]
        public bool suppressDust;

        [Header("Look")]
        [Tooltip("Dust tint (multiplied by the flipbook lighting). Alpha is a global opacity multiplier.")]
        public Color color = new Color(0.55f, 0.42f, 0.30f, 1f);

        [Range(0f, 2f)] public float densityMul = 1f;
        [Range(0.2f, 2f)] public float sizeMul = 1f;
        [Range(0.3f, 2.5f)] public float lifetimeMul = 1f;

        [Tooltip("Vertical lift of the cloud (sand/snow rise higher).")]
        [Range(0f, 3f)] public float riseMul = 1f;

        [Tooltip("Opacity of the low veil that spreads on the ground.")]
        [Range(0f, 1.5f)] public float veilMul = 1f;

        [Header("Grains")]
        [Range(0, 15)] public int grainCount = 8;
        [Tooltip("Optional mesh override; empty = the shared grain meshes of the prefab.")]
        public Mesh grainMesh;
        [Tooltip("Grain size range in metres (2-6 cm by default).")]
        public Vector2 grainSize = new Vector2(0.02f, 0.05f);
        public Color grainColor = new Color(0.45f, 0.34f, 0.24f, 1f);
        [Range(0f, 1f)] public float grainBounce = 0.3f;
        [Tooltip("Multiplies grain launch speed (snow powder is slow, stone chips fast).")]
        [Range(0.2f, 2.5f)] public float grainSpeedMul = 1f;

        [Header("Audio")]
        public AudioClip[] sfx;
        [Range(0f, 1f)] public float sfxVolume = 0.6f;

        [Header("Mapping (no prefab duplication)")]
        [Tooltip("Collider.tag values that resolve to this surface.")]
        public string[] tags;
        public PhysicsMaterial[] physicsMaterials;
        [Tooltip("Terrain layers (splat) that resolve to this surface.")]
        public TerrainLayer[] terrainLayers;

        public bool EmitsDust => !suppressDust && kind != DustSurfaceKind.None && densityMul > 0.001f;
    }
}
