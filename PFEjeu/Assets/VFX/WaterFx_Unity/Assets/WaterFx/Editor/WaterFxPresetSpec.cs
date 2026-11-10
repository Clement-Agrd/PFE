using UnityEngine;

namespace WaterFx.Editor
{
    public sealed class FloatProp { public string Name; public float Value; public FloatProp(string name, float value) { Name = name; Value = value; } }
    public sealed class ColorProp { public string Name; public Color Value; public ColorProp(string name, Color value) { Name = name; Value = value; } }
    public sealed class VectorProp { public string Name; public Vector4 Value; public VectorProp(string name, Vector4 value) { Name = name; Value = value; } }

    /// <summary>Un préréglage d'eau : taille du prefab + valeurs de propriétés du shader (mêmes valeurs que l'aperçu web).</summary>
    public sealed class WaterPresetSpec
    {
        public string Id, PrefabName, Label;
        public Vector2 Size;                       // dimensions du prefab en mètres (x, z)
        public FloatProp[] Floats;
        public ColorProp[] Colors;
        public VectorProp[] Vectors;
    }
}
