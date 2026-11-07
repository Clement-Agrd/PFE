using System.IO;
using Core.Vfx;
using UnityEditor;
using UnityEngine;

namespace Core.Vfx.EditorTools
{
    /// <summary>
    /// Construit les NOUVEAUX prefabs d'effets (portail de spawn, aura ennemie, impact, mort) a partir du profil.
    /// Les valeurs de couleur viennent du VfxProfile. Idempotent : les prefabs sont regeneres au meme chemin.
    /// </summary>
    public static class VfxGameplayPrefabBuilder
    {
        public const string Folder = "Assets/VFX/Spawn";
        private const string GlowMaterialPath = "Assets/VFX/Ambiance/M_GlowAdditive.mat";
        private const string MistMaterialPath = "Assets/VFX/Ambiance/M_MistPuff.mat";
        private const string TexturePath = "Assets/VFX/Ambiance/T_MistPuff.png";
        private const string ProfilePath = "Assets/VFX/Ambiance/VfxProfile.asset";

        public sealed class Result
        {
            public GameObject portal, binding, hit, death;
        }

        public static Result BuildAll()
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath).FullName, Folder));
            var profile = AssetDatabase.LoadAssetAtPath<VfxProfile>(ProfilePath);
            if (profile == null) throw new System.InvalidOperationException("VfxProfile introuvable : installer la brume (P1) d'abord.");
            Material glow = EnsureGlowMaterial();
            Material dust = AssetDatabase.LoadAssetAtPath<Material>(MistMaterialPath);

            return new Result
            {
                portal = SavePrefab(BuildPortal(profile, glow), "Fx_SpawnPortal"),
                binding = SavePrefab(BuildBinding(profile, glow, dust), "Fx_EnemyBinding"),
                hit = SavePrefab(BuildHit(profile, glow), "Fx_EnemyHit"),
                death = SavePrefab(BuildDeath(profile, glow, dust), "Fx_EnemyDeath"),
            };
        }

        #region Materiau

        private static Material EnsureGlowMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Core/Vfx/GlowAdditive");
                if (shader == null) throw new System.InvalidOperationException("Shader Core/Vfx/GlowAdditive introuvable.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, GlowMaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            EditorUtility.SetDirty(material);
            return material;
        }

        #endregion

        #region Prefabs

        private static GameObject BuildPortal(VfxProfile p, Material glow)
        {
            GameObject root = new GameObject("Fx_SpawnPortal");
            ParticleSystem disc = AddSystem(root.transform, "Glow", glow, ParticleSystemRenderMode.HorizontalBillboard);
            Configure(disc, lifetime: 1.4f, size: 9f, speed: 0f, rate: 1.4f, startColor: WithAlpha(p.EnemyColor, 0.85f));
            PulseAlpha(disc);
            SizeCurve(disc, 0.6f, 1.15f, 0.85f);

            ParticleSystem halo = AddSystem(root.transform, "Halo", glow, ParticleSystemRenderMode.Billboard);
            Configure(halo, lifetime: 1.4f, size: 5f, speed: 0f, rate: 1.4f, startColor: WithAlpha(Color.Lerp(p.EnemyColor, Color.white, 0.25f), 0.5f));
            halo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            PulseAlpha(halo);

            ParticleSystem embers = AddSystem(root.transform, "Embers", glow, ParticleSystemRenderMode.Billboard);
            Configure(embers, lifetime: 2.2f, size: 0.28f, speed: 3.5f, rate: 28f, startColor: WithAlpha(p.EnemyColor, 1f));
            Shape(embers, ParticleSystemShapeType.Circle, 3.2f, new Vector3(-90f, 0f, 0f));
            FadeOut(embers);
            Gravity(embers, -0.05f);

            Attach<PooledParticleFx>(root, "systems", disc, halo, embers);
            return root;
        }

        private static GameObject BuildBinding(VfxProfile p, Material glow, Material dust)
        {
            GameObject root = new GameObject("Fx_EnemyBinding");

            ParticleSystem eyes = AddSystem(root.transform, "Aura", glow, ParticleSystemRenderMode.Billboard);
            Configure(eyes, lifetime: 1.6f, size: 1.3f, speed: 0f, rate: 1.5f, startColor: WithAlpha(p.EnemyColor, 0.28f));
            PulseAlpha(eyes);
            Local(eyes);

            ParticleSystem dustPs = AddSystem(root.transform, "Dust", dust, ParticleSystemRenderMode.Billboard);
            dustPs.transform.localPosition = new Vector3(0f, -p.EnemyGlowHeight + 0.15f, 0f);
            Configure(dustPs, lifetime: 0.9f, size: 0.9f, speed: 0.4f, rate: 0f, startColor: WithAlpha(p.AmbientColor, 0.16f));
            ParticleSystem.EmissionModule em = dustPs.emission;
            em.rateOverDistance = 1.6f;
            GrowAndFade(dustPs);

            Attach<EnemyVfxBinding>(root, "systems", eyes, dustPs);
            return root;
        }

        private static GameObject BuildHit(VfxProfile p, Material glow)
        {
            GameObject root = new GameObject("Fx_EnemyHit");
            ParticleSystem sparks = AddSystem(root.transform, "Sparks", glow, ParticleSystemRenderMode.Billboard);
            Configure(sparks, lifetime: 0.35f, size: 0.14f, speed: 6f, rate: 0f, startColor: WithAlpha(Color.Lerp(p.EnemyColor, Color.white, 0.4f), 1f));
            Burst(sparks, 14);
            Shape(sparks, ParticleSystemShapeType.Sphere, 0.15f, Vector3.zero);
            Gravity(sparks, 1.5f);
            FadeOut(sparks);

            ParticleSystem flash = AddSystem(root.transform, "Flash", glow, ParticleSystemRenderMode.Billboard);
            Configure(flash, lifetime: 0.18f, size: 1.6f, speed: 0f, rate: 0f, startColor: WithAlpha(p.EnemyColor, 0.8f));
            Burst(flash, 1);
            FadeOut(flash);

            Attach<PooledParticleFx>(root, "systems", sparks, flash);
            SetLifetime(root, 0.5f);
            return root;
        }

        private static GameObject BuildDeath(VfxProfile p, Material glow, Material dust)
        {
            GameObject root = new GameObject("Fx_EnemyDeath");
            ParticleSystem embers = AddSystem(root.transform, "Embers", glow, ParticleSystemRenderMode.Billboard);
            Configure(embers, lifetime: 0.9f, size: 0.22f, speed: 3.5f, rate: 0f, startColor: WithAlpha(p.EnemyColor, 1f));
            Burst(embers, 26);
            Shape(embers, ParticleSystemShapeType.Sphere, 0.3f, Vector3.zero);
            Gravity(embers, -0.15f);
            FadeOut(embers);

            ParticleSystem burst = AddSystem(root.transform, "Burst", glow, ParticleSystemRenderMode.Billboard);
            Configure(burst, lifetime: 0.45f, size: 3.2f, speed: 0f, rate: 0f, startColor: WithAlpha(p.EnemyColor, 0.7f));
            Burst(burst, 1);
            SizeCurve(burst, 0.3f, 1f, 1.2f);
            FadeOut(burst);

            ParticleSystem smoke = AddSystem(root.transform, "Smoke", dust, ParticleSystemRenderMode.Billboard);
            Configure(smoke, lifetime: 1.1f, size: 1.4f, speed: 0.6f, rate: 0f, startColor: WithAlpha(p.AmbientColor, 0.22f));
            Burst(smoke, 5);
            Shape(smoke, ParticleSystemShapeType.Sphere, 0.4f, Vector3.zero);
            GrowAndFade(smoke);

            Attach<PooledParticleFx>(root, "systems", embers, burst, smoke);
            SetLifetime(root, 1.3f);
            return root;
        }

        #endregion

        #region Outils de configuration

        private static ParticleSystem AddSystem(Transform parent, string name, Material material, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            var ps = go.GetComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static void Configure(ParticleSystem ps, float lifetime, float size, float speed, float rate, Color startColor)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startSpeed = speed;
            main.startColor = startColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
        }

        private static void Local(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
        }

        private static void Shape(ParticleSystem ps, ParticleSystemShapeType type, float radius, Vector3 rotation)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.radius = radius;
            shape.rotation = rotation;
        }

        private static void Burst(ParticleSystem ps, short count)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        }

        private static void Gravity(ParticleSystem ps, float multiplier)
        {
            ParticleSystem.MainModule main = ps.main;
            main.gravityModifier = multiplier;
        }

        private static void PulseAlpha(ParticleSystem ps)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = g;
        }

        private static void FadeOut(ParticleSystem ps)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = g;
        }

        private static void GrowAndFade(ParticleSystem ps)
        {
            FadeOut(ps);
            SizeCurve(ps, 0.6f, 1f, 1.6f);
        }

        private static void SizeCurve(ParticleSystem ps, float a, float b, float c)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, a), new Keyframe(0.4f, b), new Keyframe(1f, c)));
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        private static void Attach<T>(GameObject root, string arrayField, params ParticleSystem[] systems) where T : Component
        {
            T component = root.AddComponent<T>();
            var so = new SerializedObject(component);
            SerializedProperty array = so.FindProperty(arrayField);
            array.arraySize = systems.Length;
            for (int i = 0; i < systems.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = systems[i];
            so.ApplyModifiedProperties();
        }

        private static void SetLifetime(GameObject root, float seconds)
        {
            var fx = root.GetComponent<PooledParticleFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("defaultLifetime").floatValue = seconds;
            so.ApplyModifiedProperties();
        }

        private static GameObject SavePrefab(GameObject root, string name)
        {
            string path = Folder + "/" + name + ".prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        #endregion
    }
}
