using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BurnFx.Editor
{
    /// <summary>
    /// Construit le prefab BurnTarget (état "brûle") avec les matériaux du kit Fire déjà dans le projet.
    ///   Tools > Burn FX > 1 Construire le prefab BurnTarget
    /// Sortie : Assets/VFX/BurnFx/Prefabs/BurnTarget.prefab (écrasé à chaque relance, GUID conservé).
    /// Le prefab se pose en enfant de la cible (pivot aux pieds) ; BurnTargetFx mesure le corps et cale l'émission dessus.
    /// </summary>
    public static class BurnFxBuilder
    {
        #region Chemins
        private const string FireMaterials = "Assets/VFX/Fire/Materials/";
        private const string PrefabsDir = "Assets/VFX/BurnFx/Prefabs";
        private const string MaterialsDir = "Assets/VFX/BurnFx/Materials";
        private const string PrefabPath = PrefabsDir + "/BurnTarget.prefab";
        #endregion

        [MenuItem("Tools/Burn FX/1 Construire le prefab BurnTarget")]
        public static void BuildPrefab()
        {
            Material flameMat = LoadFire("M_FlameFlip");
            Material flameFineMat = LoadFire("M_FlameFlipB");
            Material smokeMat = LoadFire("M_SmokeFlip");
            Material emberMat = LoadFire("M_Spark");
            if (flameMat == null || flameFineMat == null || smokeMat == null || emberMat == null)
            {
                Debug.LogError("[BurnFx] Matériaux du kit Fire introuvables dans " + FireMaterials + " : lance d'abord Tools > Fire VFX > Build All.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(PrefabsDir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/VFX/BurnFx")) AssetDatabase.CreateFolder("Assets/VFX", "BurnFx");
                AssetDatabase.CreateFolder("Assets/VFX/BurnFx", "Prefabs");
            }

            // Copies des matériaux de flamme avec un fondu à la géométrie très court : les flammes posées sur le corps ne doivent pas s effacer contre lui.
            if (!AssetDatabase.IsValidFolder(MaterialsDir)) AssetDatabase.CreateFolder("Assets/VFX/BurnFx", "Materials");
            flameMat = LoadOrCopy(flameMat, "M_BurnFlame");
            flameFineMat = LoadOrCopy(flameFineMat, "M_BurnFlameFine");

            var root = new GameObject("BurnTarget");

            // Flammes : émises dans le volume du corps (la boîte est recalée par BurnTargetFx), elles montent en s'effilant.
            ParticleSystem flames = CreateSystem("Flames", root.transform, flameMat);
            ConfigureFlames(flames, 0.6f, 1.0f, 1.2f, 2.1f, 0.9f, 1.5f);
            Flipbook(flames, 0.3f);

            ParticleSystem fineFlames = CreateSystem("FlamesFine", root.transform, flameFineMat);
            ConfigureFlames(fineFlames, 0.4f, 0.7f, 0.7f, 1.2f, 1.2f, 2.0f);
            Flipbook(fineFlames, 0.3f);

            // Fumée : naît vers le haut du corps, monte lentement, grossit et se disperse.
            ParticleSystem smoke = CreateSystem("Smoke", root.transform, smokeMat);
            ConfigureSmoke(smoke);
            Flipbook(smoke, 0f);

            // Braises : petites étincelles qui s'envolent dans tous les sens, puis s'éteignent.
            ParticleSystem embers = CreateSystem("Embers", root.transform, emberMat);
            ConfigureEmbers(embers);

            // Lueur orange qui éclaire le sol et les voisins.
            var lightObject = new GameObject("GlowLight");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 1f, 0f);
            Light glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.52f, 0.16f);
            glow.range = 4.5f;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;
            glow.enabled = false;

            var fx = root.AddComponent<BurnTargetFx>();
            fx.SetReferences(flames, fineFlames, smoke, embers, glow);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[BurnFx] Prefab créé : " + PrefabPath);
        }

        #region Pose dans la scène
        /// <summary>Pose BurnTarget en enfant de la cible et le câble ; renvoie le composant BurnTargetFx.</summary>
        public static BurnTargetFx AttachTo(GameObject target)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[BurnFx] Prefab introuvable : lance Tools > Burn FX > 1 Construire le prefab BurnTarget.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(instance, "Attach BurnTarget");
            return instance.GetComponent<BurnTargetFx>();
        }
        #endregion

        #region Systèmes de particules
        // Copie (une fois) un matériau du kit Fire dans le dossier BurnFx et règle son fondu à la géométrie ; le kit Fire reste intact.
        private static Material LoadOrCopy(Material source, string name)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(source) { name = name };
                AssetDatabase.CreateAsset(copy, path);
            }
            copy.SetFloat("_SoftDist", 0.1f);
            copy.SetFloat("_NearFade", 0.2f);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static Material LoadFire(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(FireMaterials + name + ".mat");
        }

        private static ParticleSystem CreateSystem(string name, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // les modules ne se règlent pas pendant la lecture

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = material;
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            return system;
        }

        private static void ConfigureFlames(ParticleSystem system, float lifeMin, float lifeMax, float sizeMin, float sizeMax, float riseMin, float riseMax)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            main.startColor = Color.white;
            main.maxParticles = 200;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;     // piloté par BurnTargetFx

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 1f, 0f);
            shape.scale = new Vector3(0.8f, 1.6f, 0.5f);

            // IMPORTANT : les 3 axes dans le même mode (TwoConstants), sinon Unity lève une erreur.
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            velocity.y = new ParticleSystem.MinMaxCurve(riseMin, riseMax);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

            EnvelopeAlpha(system, 0.12f, 0.45f);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.35f)));
        }

        private static void ConfigureSmoke(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.55f, 0.52f, 0.5f, 0.75f);   // rgb = teinte de la fumée, alpha = enveloppe de vie
            main.maxParticles = 120;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 1.8f, 0f);
            shape.scale = new Vector3(0.4f, 0.15f, 0.3f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            EnvelopeAlpha(system, 0.2f, 0.5f);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.5f)));
        }

        private static void ConfigureEmbers(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startColor = Color.white;     // la couleur HDR vient du matériau M_Spark
            main.maxParticles = 160;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 1f, 0f);
            shape.scale = new Vector3(0.8f, 1.6f, 0.5f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            velocity.y = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

            EnvelopeAlpha(system, 0.1f, 0.5f);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));
        }

        // Texture Sheet Animation 8x8 (toute la feuille) avec frame blending : mêmes vertex streams que le kit Fire.
        private static void Flipbook(ParticleSystem system, float pivotY)
        {
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 8;
            sheet.numTilesY = 8;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
            sheet.cycleCount = 1;

            var particleRenderer = system.GetComponent<ParticleSystemRenderer>();
            particleRenderer.pivot = new Vector3(0f, pivotY, 0f);
            particleRenderer.sortMode = ParticleSystemSortMode.Distance;
            particleRenderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.UV2, ParticleSystemVertexStream.AnimBlend
            });
        }

        // L'alpha de la couleur de particule = enveloppe de vie : monte, tient, retombe à 0 (le shader du kit Fire s'en sert).
        private static void EnvelopeAlpha(ParticleSystem system, float fadeInEnd, float fadeOutStart)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd),
                    new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }
        #endregion
    }
}
