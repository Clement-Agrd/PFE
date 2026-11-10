using UnityEngine;

namespace StudioFx.Editor
{
    public enum EmitterShape { Circle, Box }

    /// <summary>Famille de rendu : choisit le shader, la texture et la configuration du Particle System.</summary>
    public enum EmitterKind
    {
        Fire,    // flipbook de chaleur + rampe de couleur, additif HDR
        Smoke,   // flipbook de fumée éclairée, alpha
        Glow,    // sprite doux additif HDR (braises, étincelles, halos), éventuellement étiré
        Haze     // distorsion de chaleur (nécessite Opaque Texture)
    }

    public sealed class FloatProp { public string Name; public float Value; public FloatProp(string name, float value) { Name = name; Value = value; } }
    public sealed class ColorProp { public string Name; public Color Value; public ColorProp(string name, Color value) { Name = name; Value = value; } }

    /// <summary>
    /// Description d'UN système de particules (une couche d'un FX). Mêmes valeurs que l'aperçu web.
    /// Vitesses : par axe, plage [min,max] en m/s (espace local du prefab).
    /// </summary>
    public sealed class EmitterSpec
    {
        public string Name;
        public EmitterKind Kind;
        public string Texture;                 // "fire" | "smoke" | "soft"
        public float Rate;                     // particules / s (0 si rafales)
        public float BurstInterval;            // > 0 : rafale toutes les N secondes
        public int BurstMin, BurstMax;
        public Vector2 Life;
        public EmitterShape Shape;
        public float Radius;                   // Circle : disque posé à plat
        public Vector3 BoxSize;                // Box
        public Vector3 Offset;
        public Vector2 VelX, VelY, VelZ;
        public float Gravity;
        public Vector2 Size;
        public Vector2[] SizeCurve;            // (t, facteur)
        public Color[] Colors;                 // teinte sur la vie (Glow) ; blanc pour les flipbooks
        public float Alpha, FadeIn, FadeOut;
        public float NoiseStrength, NoiseFrequency;
        public bool WorldSpace;
        public bool RandomRotation;
        public float Stretch;                  // > 0 : billboard étiré, valeur = velocityScale
        public float FlipRange;                // part du flipbook jouée sur la vie (1 = tout)
        public bool FlipRandomStart;           // image de départ aléatoire (boucles de feu)
        public FloatProp[] MaterialFloats;
        public ColorProp[] MaterialColors;
    }

    public sealed class LightSpec
    {
        public Color Color;
        public float Intensity, Range;
        public Vector3 Offset;
        public float Flicker, Speed;
    }

    public sealed class FxSpec
    {
        public string Id, PrefabName, Label;
        public EmitterSpec[] Emitters;
        public LightSpec Light;
    }
}
