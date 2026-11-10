using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FreezeFx.Editor
{
    /// <summary>
    /// Construit le prefab FrozenTarget (état "glacé") avec les meshes et matériaux du kit Ice déjà dans le projet.
    ///   Tools > Freeze FX > 1 Construire le prefab FrozenTarget
    /// Sortie : Assets/VFX/FreezeFx/Prefabs/FrozenTarget.prefab (écrasé à chaque relance, GUID conservé).
    /// Le prefab se pose en enfant de la cible ; FrozenTargetFx mesure son corps et y place cristaux, givre, brume et lumière.
    /// </summary>
    public static class FreezeFxBuilder
    {
        #region Chemins et réglages
        private const string IceMaterials = "Assets/VFX/Ice/Materials/";
        private const string IceMeshes = "Assets/VFX/Ice/Meshes/";
        private const string PrefabsDir = "Assets/VFX/FreezeFx/Prefabs";
        private const string PrefabPath = PrefabsDir + "/FrozenTarget.prefab";

        private const int SpikeCount = 0;           // pas de piques : seulement la brume et l aura bleutée (remets 18 pour les cristaux)
        private const int ClusterEvery = 5;         // un cluster de cristaux tous les 5 éléments, des pointes sinon
        private const int RandomSeed = 20260512;
        #endregion

        [MenuItem("Tools/Freeze FX/1 Construire le prefab FrozenTarget")]
        public static void BuildPrefab()
        {
            Material iceGlass = LoadMaterial("M_IceGlass");
            Material frost = LoadMaterial("M_Frost");
            Material mistMat = LoadMaterial("M_Mist");
            Material sparkMat = LoadMaterial("M_Spark");
            Material shardMat = LoadMaterial("M_Shard");
            Mesh spikeMesh = AssetDatabase.LoadAssetAtPath<Mesh>(IceMeshes + "IceSpike.asset");
            Mesh clusterMesh = AssetDatabase.LoadAssetAtPath<Mesh>(IceMeshes + "IceCrystalCluster.asset");
            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            if (iceGlass == null || frost == null || mistMat == null || sparkMat == null || shardMat == null || spikeMesh == null || clusterMesh == null)
            {
                Debug.LogError("[FreezeFx] Assets du kit Ice introuvables dans Assets/VFX/Ice : lance d'abord le builder du kit (Tools > Ice VFX).");
                return;
            }

            if (!AssetDatabase.IsValidFolder(PrefabsDir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/VFX/FreezeFx")) AssetDatabase.CreateFolder("Assets/VFX", "FreezeFx");
                AssetDatabase.CreateFolder("Assets/VFX/FreezeFx", "Prefabs");
            }

            var root = new GameObject("FrozenTarget");

            // (1) cristaux de glace : placés et mis à l'échelle par FrozenTargetFx selon le corps de la cible
            var spikesParent = new GameObject("IceSpikes");
            spikesParent.transform.SetParent(root.transform, false);
            var spikeRoots = new Transform[SpikeCount];
            var spikeRenderers = new Renderer[SpikeCount];
            var spikeScales = new Vector3[SpikeCount];
            var random = new System.Random(RandomSeed);
            for (int i = 0; i < SpikeCount; i++)
            {
                bool isCluster = i % ClusterEvery == ClusterEvery - 1;
                var spike = new GameObject(isCluster ? "Cluster_" + i : "Spike_" + i);
                spike.transform.SetParent(spikesParent.transform, false);
                spike.AddComponent<MeshFilter>().sharedMesh = isCluster ? clusterMesh : spikeMesh;
                var meshRenderer = spike.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = iceGlass;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.enabled = false;

                float width = Range(random, isCluster ? 0.24f : 0.13f, isCluster ? 0.36f : 0.24f);
                float height = Range(random, isCluster ? 0.28f : 0.45f, isCluster ? 0.42f : 0.85f);
                spikeRoots[i] = spike.transform;
                spikeRenderers[i] = meshRenderer;
                spikeScales[i] = new Vector3(width, height, width);
            }

            // (2) givre au sol : quad à plat dont FrozenTargetFx règle la taille et l'alpha
            var frostObject = new GameObject("GroundFrost");
            frostObject.transform.SetParent(root.transform, false);
            frostObject.AddComponent<MeshFilter>().sharedMesh = quad;
            var frostRenderer = frostObject.AddComponent<MeshRenderer>();
            frostRenderer.sharedMaterial = frost;
            frostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            frostRenderer.receiveShadows = false;
            frostRenderer.enabled = false;

            // (3) brume froide, (4) étincelles bleues, (5) éclats projetés au gel et au dégel
            ParticleSystem mist = CreateSystem("Mist", root.transform, mistMat);
            ConfigureMist(mist);
            ParticleSystem sparkles = CreateSystem("Sparkles", root.transform, sparkMat);
            ConfigureSparkles(sparkles);
            ParticleSystem shards = CreateSystem("ShardBurst", root.transform, shardMat);
            ConfigureShards(shards);

            // Aura bleutée : grosses bouffées lumineuses bleues, lentes et douces, autour du corps.
            ParticleSystem aura = CreateSystem("AuraGlow", root.transform, LoadMaterial("M_MistGlow"));
            ConfigureAura(aura);

            // (6) lueur froide
            var lightObject = new GameObject("ColdLight");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 1f, 0f);
            Light cold = lightObject.AddComponent<Light>();
            cold.type = LightType.Point;
            cold.color = new Color(0.4f, 0.72f, 1f);
            cold.range = 5f;
            cold.intensity = 0f;
            cold.shadows = LightShadows.None;
            cold.enabled = false;

            var fx = root.AddComponent<FrozenTargetFx>();
            fx.SetReferences(spikeRoots, spikeRenderers, spikeScales, frostRenderer, mist, sparkles, shards, cold);
            fx.SetAuraReference(aura);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[FreezeFx] Prefab créé : " + PrefabPath);
        }

        #region Pose dans la scène
        /// <summary>Pose FrozenTarget en enfant de la cible et renvoie le composant FrozenTargetFx.</summary>
        public static FrozenTargetFx AttachTo(GameObject target)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[FreezeFx] Prefab introuvable : lance Tools > Freeze FX > 1 Construire le prefab FrozenTarget.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(instance, "Attach FrozenTarget");
            return instance.GetComponent<FrozenTargetFx>();
        }
        #endregion

        #region Systèmes de particules
        private static Material LoadMaterial(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(IceMaterials + name + ".mat");
        }

        private static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
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

        // Brume froide : grosses bouffées lentes qui dérivent autour du corps, vers le bas puis vers l'extérieur (l'air froid retombe).
        private static void ConfigureMist(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.9f, 0.97f, 1f, 0.45f);
            main.maxParticles = 120;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;              // piloté par FrozenTargetFx

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 0.4f, 0f);
            shape.scale = new Vector3(1.4f, 0.8f, 1.4f);

            // IMPORTANT : les 3 axes dans le même mode (TwoConstants), sinon Unity lève une erreur.
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.25f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            EnvelopeAlpha(system, 0.25f, 0.55f);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f)));
        }

        // Halo bleu : peu de grosses bouffées très douces (alpha bas), qui montent à peine et grossissent : une aura, pas des nuages.
        private static void ConfigureAura(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, 0.4f);
            main.maxParticles = 80;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;              // piloté par FrozenTargetFx

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 1f, 0f);
            shape.scale = new Vector3(0.8f, 1.8f, 0.8f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);

            EnvelopeAlpha(system, 0.3f, 0.6f);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.3f)));
        }

        // Étincelles de givre : minuscules points bleus HDR qui scintillent autour du corps.
        private static void ConfigureSparkles(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = Color.white;           // la couleur HDR vient du matériau M_Spark
            main.maxParticles = 160;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 1f, 0f);
            shape.scale = new Vector3(1.4f, 2f, 1.4f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.2f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

            // Scintillement : l'alpha oscille plusieurs fois pendant la vie de l'étincelle.
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.2f, 0.3f),
                    new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0.15f, 0.62f), new GradientAlphaKey(0.9f, 0.78f),
                    new GradientAlphaKey(0.3f, 0.9f), new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        // Éclats : projetés vers l'extérieur au gel et au dégel (émis à la demande par FrozenTargetFx), retombent avec la gravité.
        private static void ConfigureShards(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = 1.2f;
            main.maxParticles = 120;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;              // émission uniquement par FrozenTargetFx (Emit)

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;

            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            EnvelopeAlpha(system, 0.05f, 0.6f);
        }

        // L'alpha de la couleur de particule : monte, tient, retombe à 0.
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
