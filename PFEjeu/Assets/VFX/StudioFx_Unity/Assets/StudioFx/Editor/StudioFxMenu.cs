using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioFx.Editor
{
    /// <summary>
    /// Installeur : construit les 6 prefabs réalistes depuis StudioFxSpecs (Particle System créé par code).
    ///   Tools > Studio FX > 1 Installer       : matériaux + prefabs dans Assets/StudioFx
    ///   Tools > Studio FX > 2 Scène de démo   : pose tous les FX en ligne dans la scène ouverte
    ///   Tools > Studio FX > 3 Vérifier URP    : contrôle Depth Texture, Opaque Texture et HDR
    /// Relancer l'installeur ÉCRASE les prefabs FX_* (GUID conservé) et ré-applique les valeurs des matériaux.
    /// </summary>
    public static class StudioFxMenu
    {
        #region Constantes
        private const string Root = "Assets/StudioFx";
        private const string MaterialsDir = Root + "/Materials";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string TexturesDir = Root + "/Textures";
        private const int SheetTiles = 8;                 // flipbooks 8 x 8
        private const float DemoSpacing = 6f;
        #endregion

        #region Menu
        [MenuItem("Tools/Studio FX/1 Installer (matériaux + prefabs)")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            EnsureFolder(MaterialsDir);
            EnsureFolder(PrefabsDir);

            foreach (FxSpec spec in StudioFxSpecs.All)
                BuildPrefab(spec);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[StudioFx] " + StudioFxSpecs.All.Length + " prefabs créés dans " + PrefabsDir + ". Lance aussi 'Vérifier URP'.");
        }

        [MenuItem("Tools/Studio FX/2 Scène de démo (tous les FX en ligne)")]
        public static void BuildDemo()
        {
            var group = new GameObject("Studio FX démo");
            Undo.RegisterCreatedObjectUndo(group, "Studio FX demo");

            int index = 0;
            foreach (FxSpec spec in StudioFxSpecs.All)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/" + spec.PrefabName + ".prefab");
                if (prefab == null)
                {
                    Debug.LogError("[StudioFx] Prefab manquant : " + spec.PrefabName + ". Lance d'abord Tools > Studio FX > 1 Installer.");
                    return;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                instance.transform.localPosition = new Vector3(index * DemoSpacing, 0f, 0f);
                index++;
            }
        }

        [MenuItem("Tools/Studio FX/3 Vérifier URP (depth, opaque, HDR)")]
        public static void CheckUrp()
        {
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
            {
                Debug.LogError("[StudioFx] Aucun Render Pipeline Asset actif : le kit cible URP.");
                return;
            }

            var so = new SerializedObject(asset);
            Report(so, "m_RequireDepthTexture", "Depth Texture", "particules soft (fondu avec le décor)");
            Report(so, "m_RequireOpaqueTexture", "Opaque Texture", "distorsion de chaleur (HeatHaze)");
            Report(so, "m_SupportsHDR", "HDR", "intensités > 1 -> bloom sur le feu et les braises");
            Debug.Log("[StudioFx] Pour le bloom : ajoute un Volume avec l'override Bloom (seuil ~1) et active Post Processing sur la caméra.");
        }
        #endregion

        #region Vérification URP
        private static void Report(SerializedObject so, string property, string label, string usage)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p == null) Debug.LogWarning("[StudioFx] " + label + " : réglage introuvable dans cette version d'URP, vérifie-le à la main (" + usage + ").");
            else if (p.boolValue) Debug.Log("[StudioFx] " + label + " : OK.");
            else Debug.LogWarning("[StudioFx] " + label + " est DÉSACTIVÉ dans l'asset URP : active-le pour " + usage + ".");
        }
        #endregion

        #region Construction d'un prefab
        private static void BuildPrefab(FxSpec spec)
        {
            var root = new GameObject(spec.PrefabName);
            var systems = new List<ParticleSystem>();
            var flickers = new List<StudioLightFlicker>();

            foreach (EmitterSpec emitter in spec.Emitters)
                systems.Add(BuildEmitter(emitter, root.transform, GetMaterial(spec, emitter)));

            if (spec.Light != null)
                flickers.Add(BuildLight(spec.Light, root.transform));

            var controller = root.AddComponent<StudioFxController>();
            controller.SetReferences(systems.ToArray(), flickers.ToArray());

            PrefabUtility.SaveAsPrefabAsset(root, PrefabsDir + "/" + spec.PrefabName + ".prefab");
            Object.DestroyImmediate(root);
        }

        private static ParticleSystem BuildEmitter(EmitterSpec e, Transform parent, Material material)
        {
            var go = new GameObject(e.Name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // les modules ne se règlent pas pendant la lecture

            bool isBurst = e.BurstInterval > 0f;
            bool isFlipbook = e.Kind != EmitterKind.Glow;

            // --- Main
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = isBurst ? e.BurstInterval : 5f;
            main.prewarm = !isBurst;                                   // l'ambiance est déjà "installée" au chargement
            main.simulationSpace = e.WorldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(e.Life.x, e.Life.y);
            main.startSpeed = 0f;                                      // tout le mouvement passe par Velocity over Lifetime
            main.startSize = new ParticleSystem.MinMaxCurve(e.Size.x, e.Size.y);
            main.startColor = new Color(1f, 1f, 1f, e.Alpha);          // opacité max ici, teinte dans Color over Lifetime
            main.gravityModifier = e.Gravity;
            if (e.RandomRotation) main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            float expected = isBurst ? e.BurstMax * (e.Life.y / e.BurstInterval + 1f) : e.Rate * e.Life.y;
            main.maxParticles = Mathf.CeilToInt(expected * 1.4f) + 16;

            // --- Émission
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = isBurst ? 0f : e.Rate;
            if (isBurst)
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)e.BurstMin, (short)e.BurstMax) });

            // --- Forme
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.position = e.Offset;
            if (e.Shape == EmitterShape.Circle)
            {
                shape.shapeType = ParticleSystemShapeType.Circle;      // disque posé à plat (rotation -90° en X)
                shape.radius = e.Radius;
                shape.radiusThickness = 1f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = e.BoxSize;
            }

            // --- Mouvement : IMPORTANT, les 3 axes doivent être dans le même mode (TwoConstants), sinon Unity lève une erreur.
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(e.VelX.x, e.VelX.y);
            velocity.y = new ParticleSystem.MinMaxCurve(e.VelY.x, e.VelY.y);
            velocity.z = new ParticleSystem.MinMaxCurve(e.VelZ.x, e.VelZ.y);

            if (e.NoiseStrength > 0f)
            {
                ParticleSystem.NoiseModule noise = system.noise;
                noise.enabled = true;
                noise.strength = e.NoiseStrength;
                noise.frequency = e.NoiseFrequency;
                noise.quality = ParticleSystemNoiseQuality.Low;
            }

            // --- Taille / couleur sur la vie
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, BuildCurve(e.SizeCurve));

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(BuildGradient(e));

            // --- Flipbook : image courante + image suivante + fondu entre les deux (lissage de l'animation)
            if (isFlipbook) ConfigureFlipbook(system, e);

            // --- Rendu
            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = material;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            if (e.Stretch > 0f)
            {
                particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;   // traînées (étincelles, braises)
                particleRenderer.velocityScale = e.Stretch;
                particleRenderer.lengthScale = 1f;
                particleRenderer.cameraVelocityScale = 0f;
            }
            else
            {
                particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            if (isFlipbook)
            {
                // Les shaders lisent TEXCOORD0.xy / .zw / TEXCOORD1.x : ces 5 streams, dans cet ordre, sont obligatoires.
                particleRenderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                {
                    ParticleSystemVertexStream.Position,
                    ParticleSystemVertexStream.Color,
                    ParticleSystemVertexStream.UV,
                    ParticleSystemVertexStream.UV2,
                    ParticleSystemVertexStream.AnimBlend
                });
            }
            return system;
        }

        private static void ConfigureFlipbook(ParticleSystem system, EmitterSpec e)
        {
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = SheetTiles;
            sheet.numTilesY = SheetTiles;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            sheet.cycleCount = 1;
            // FlipRange = part de la planche jouée pendant la vie (feu : ~0,3 ; fumée : 1 = toute la bouffée).
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, e.FlipRange));
            sheet.startFrame = e.FlipRandomStart ? new ParticleSystem.MinMaxCurve(0f, 1f) : new ParticleSystem.MinMaxCurve(0f);
        }

        private static StudioLightFlicker BuildLight(LightSpec spec, Transform parent)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = spec.Offset;

            var pointLight = go.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = spec.Color;
            pointLight.intensity = spec.Intensity;
            pointLight.range = spec.Range;
            pointLight.shadows = LightShadows.None;

            var flicker = go.AddComponent<StudioLightFlicker>();
            flicker.Configure(spec.Flicker, spec.Speed);
            return flicker;
        }
        #endregion

        #region Courbes / dégradés
        private static AnimationCurve BuildCurve(Vector2[] points)
        {
            var keys = new Keyframe[points.Length];
            for (int i = 0; i < points.Length; i++) keys[i] = new Keyframe(points[i].x, points[i].y);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        // Couleurs réparties uniformément ; alpha : 0 -> 1 (FadeIn) -> 1 (FadeOut) -> 0.
        private static Gradient BuildGradient(EmitterSpec e)
        {
            int n = e.Colors.Length;
            var colorKeys = new GradientColorKey[n];
            for (int i = 0; i < n; i++)
                colorKeys[i] = new GradientColorKey(e.Colors[i], n == 1 ? 0f : i / (float)(n - 1));

            float fadeIn = Mathf.Clamp(e.FadeIn, 0.01f, 0.98f);
            float fadeOut = Mathf.Clamp(e.FadeOut, fadeIn + 0.01f, 0.99f);
            var alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn),
                new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f)
            };

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }
        #endregion

        #region Matériaux / dossiers
        // Un matériau par émetteur (chaque couche a ses propres réglages). Valeurs ré-appliquées à chaque installation.
        private static Material GetMaterial(FxSpec spec, EmitterSpec e)
        {
            Shader shader = Shader.Find(ShaderName(e.Kind));
            if (shader == null) Debug.LogError("[StudioFx] Shader introuvable : " + ShaderName(e.Kind) + " (regarde la Console, URP installé ?).");

            string name = "M_Studio_" + spec.Id + "_" + e.Name;
            string path = MaterialsDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture("_MainTex", LoadTexture(SheetName(e)));
            if (e.Kind == EmitterKind.Fire) material.SetTexture("_RampTex", LoadTexture("T_StudioRamp_Fire"));
            if (e.MaterialFloats != null) foreach (FloatProp p in e.MaterialFloats) material.SetFloat(p.Name, p.Value);
            if (e.MaterialColors != null) foreach (ColorProp p in e.MaterialColors) material.SetColor(p.Name, p.Value);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static string ShaderName(EmitterKind kind)
        {
            switch (kind)
            {
                case EmitterKind.Fire: return "StudioFx/Fire";
                case EmitterKind.Smoke: return "StudioFx/Smoke";
                case EmitterKind.Haze: return "StudioFx/HeatHaze";
                default: return "StudioFx/Glow";
            }
        }

        private static string SheetName(EmitterSpec e)
        {
            switch (e.Texture)
            {
                case "fire": return "T_StudioFire_8x8";
                case "smoke": return "T_StudioSmoke_8x8";
                default: return "T_StudioSoft";
            }
        }

        private static Texture2D LoadTexture(string name)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/" + name + ".png");
            if (texture == null) Debug.LogWarning("[StudioFx] Texture manquante : " + name);
            return texture;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        #endregion
    }
}
