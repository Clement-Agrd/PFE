using System.IO;
using UnityEditor;
using UnityEngine;

namespace HealFx.Editor
{
    /// <summary>
    /// Installeur : crée matériaux, palettes, meshes et prefabs prêts à l'emploi (Particle System construit par code).
    ///   Tools > Heal FX > 0 Générer les textures    : volutes (flipbook 8x8) + atlas de runes  (HealFxTextureGenerator)
    ///   Tools > Heal FX > 1 Installer               : génère tout dans le dossier du kit
    ///   Tools > Heal FX > 2 Scène de démo           : pose 2 zones (alliée / ennemie) avec des unités factices dans la scène ouverte
    ///   Tools > Heal FX > 3 Prefab Training Room    : crée TrainingRoom_HealFx (zone + mannequin + boucle + panneau de réglage)
    ///
    /// Sortie :
    ///   Prefabs/HealZone_Ally, HealZone_Enemy      (zone au sol : à poser, appeler HealZoneFx.Pulse() à chaque tick)
    ///   Prefabs/HealPulse_Ally, HealPulse_Enemy    (sur chaque cible : appeler HealTargetFx.Pulse() à chaque soin reçu)
    ///   Prefabs/TrainingRoom_HealFx                (démo en boucle réglable en direct)
    /// Relancer l'installeur ÉCRASE les prefabs (GUID conservé) ; matériaux, palettes et meshes ne sont créés que s'ils manquent
    /// (leurs textures manquantes sont rebranchées).
    /// </summary>
    public static class HealFxMenu
    {
        #region Chemins
        // Le kit vit dans Assets/VFX/HealFx/Assets/HealFx : les assets générés restent à côté des textures.
        private const string Root = "Assets/VFX/HealFx/Assets/HealFx";
        private const string MaterialsDir = Root + "/Materials";
        private const string PalettesDir = Root + "/Palettes";
        private const string MeshesDir = Root + "/Meshes";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string TexturesDir = Root + "/Textures";
        private const string TrainingRoomPrefab = "TrainingRoom_HealFx";
        #endregion

        #region Menu
        [MenuItem("Tools/Heal FX/1 Installer (matériaux + prefabs)")]
        public static void Install()
        {
            if (!HealFxTextureGenerator.TexturesExist()) HealFxTextureGenerator.GenerateAll();

            AssetDatabase.Refresh();
            foreach (string dir in new[] { MaterialsDir, PalettesDir, MeshesDir, PrefabsDir })
                EnsureFolder(dir);

            Shader groundShader = Shader.Find("HealFx/ZoneGround");
            Shader particleShader = Shader.Find("HealFx/Particle");
            Shader columnShader = Shader.Find("HealFx/Column");
            if (groundShader == null || particleShader == null || columnShader == null)
            {
                Debug.LogError("[HealFx] Shaders introuvables ou en erreur : regarde la Console (URP installé ? 'Core.hlsl' trouvé ?).");
                return;
            }

            Material groundMat = LoadOrCreateMaterial("M_HealZoneGround", groundShader, "_RuneAtlas", TexturesDir + "/T_HealRuneAtlas.png");
            Material columnMat = LoadOrCreateMaterial("M_HealColumn", columnShader, null, null);
            Material crossMat = LoadOrCreateMaterial("M_HealCross", particleShader, "_MainTex", TexturesDir + "/T_HealCross.png");
            Material sparkleMat = LoadOrCreateMaterial("M_HealSparkle", particleShader, "_MainTex", TexturesDir + "/T_HealSparkle.png");
            Material ringMat = LoadOrCreateMaterial("M_HealRing", particleShader, "_MainTex", TexturesDir + "/T_HealRing.png");
            Material smokeMat = LoadOrCreateMaterial("M_HealSmoke", particleShader, "_MainTex", TexturesDir + "/T_HealSmokeFlipbook.png");
            Material spiralMat = LoadOrCreateMaterial("M_HealSpiral", particleShader, "_MainTex", TexturesDir + "/T_HealGlow.png");

            // Soft particles partout ; érosion uniquement sur les volutes.
            SetFloat(crossMat, "_SoftDistance", 0.3f);
            SetFloat(sparkleMat, "_SoftDistance", 0.2f);
            SetFloat(ringMat, "_SoftDistance", 0f);        // l'anneau au pied de la cible est à plat sur le sol : pas de soft depth
            SetFloat(smokeMat, "_SoftDistance", 0.8f);
            SetFloat(smokeMat, "_Erosion", 0.85f);
            SetFloat(spiralMat, "_SoftDistance", 0.25f);

            HealFxPalette ally = LoadOrCreatePalette("HealPalette_Ally", new Color(0.30f, 1.00f, 0.50f), new Color(1.00f, 0.88f, 0.45f),
                new Color(2.4f, 2.0f, 1.1f), new Color(0.55f, 1.00f, 0.65f));
            HealFxPalette enemy = LoadOrCreatePalette("HealPalette_Enemy", new Color(0.72f, 0.32f, 1.00f), new Color(1.00f, 0.42f, 0.78f),
                new Color(2.2f, 1.2f, 2.4f), new Color(0.80f, 0.45f, 1.00f));
            Mesh quad = LoadOrCreateQuadMesh();
            Mesh column = LoadOrCreateColumnMesh();

            BuildZonePrefab("HealZone_Ally", ally, quad, column, groundMat, columnMat, crossMat, sparkleMat, smokeMat, spiralMat);
            BuildZonePrefab("HealZone_Enemy", enemy, quad, column, groundMat, columnMat, crossMat, sparkleMat, smokeMat, spiralMat);
            BuildTargetPrefab("HealPulse_Ally", ally, ringMat, crossMat, sparkleMat, spiralMat);
            BuildTargetPrefab("HealPulse_Enemy", enemy, ringMat, crossMat, sparkleMat, spiralMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[HealFx] Installation terminée. Prefabs dans " + PrefabsDir + ". Teste avec Tools > Heal FX > 3 Prefab Training Room.");
        }

        [MenuItem("Tools/Heal FX/2 Scène de démo (zones alliée + ennemie)")]
        public static void BuildDemo()
        {
            GameObject allyZone = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/HealZone_Ally.prefab");
            GameObject enemyZone = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/HealZone_Enemy.prefab");
            GameObject allyPulse = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/HealPulse_Ally.prefab");
            GameObject enemyPulse = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/HealPulse_Enemy.prefab");
            if (allyZone == null || enemyZone == null || allyPulse == null || enemyPulse == null)
            {
                Debug.LogError("[HealFx] Lance d'abord Tools > Heal FX > 1 Installer.");
                return;
            }

            SpawnDemoGroup(allyZone, allyPulse, new Vector3(-4.6f, 0f, 0f), "Démo alliée");
            SpawnDemoGroup(enemyZone, enemyPulse, new Vector3(4.6f, 0f, 0f), "Démo ennemie");
        }

        [MenuItem("Tools/Heal FX/3 Prefab Training Room (zone + mannequin + boucle + panneau)")]
        public static void BuildTrainingRoomPrefab()
        {
            var zonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/HealZone_Ally.prefab");
            var pulsePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/HealPulse_Ally.prefab");
            var allyPalette = AssetDatabase.LoadAssetAtPath<HealFxPalette>(PalettesDir + "/HealPalette_Ally.asset");
            var enemyPalette = AssetDatabase.LoadAssetAtPath<HealFxPalette>(PalettesDir + "/HealPalette_Enemy.asset");
            if (zonePrefab == null || pulsePrefab == null || allyPalette == null || enemyPalette == null)
            {
                Debug.LogError("[HealFx] Lance d'abord Tools > Heal FX > 1 Installer.");
                return;
            }

            var root = new GameObject(TrainingRoomPrefab);

            var zoneObject = (GameObject)PrefabUtility.InstantiatePrefab(zonePrefab, root.transform);
            zoneObject.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            var zone = zoneObject.GetComponent<HealZoneFx>();
            zone.SetAutoPulse(true);

            // Mannequin cible, posé dans la zone : reçoit HealTargetFx à chaque impulsion.
            var mannequin = new GameObject("Mannequin_Cible");
            mannequin.transform.SetParent(root.transform, false);
            mannequin.transform.localPosition = new Vector3(1.7f, 0f, 0.9f);
            AddPrimitive(mannequin.transform, PrimitiveType.Cylinder, "Socle", new Vector3(0f, 0.04f, 0f), new Vector3(0.8f, 0.04f, 0.8f));
            AddPrimitive(mannequin.transform, PrimitiveType.Capsule, "Corps", new Vector3(0f, 0.95f, 0f), new Vector3(0.5f, 0.9f, 0.5f));
            AddPrimitive(mannequin.transform, PrimitiveType.Sphere, "Tete", new Vector3(0f, 2.0f, 0f), new Vector3(0.38f, 0.38f, 0.38f));

            var pulseObject = (GameObject)PrefabUtility.InstantiatePrefab(pulsePrefab, mannequin.transform);
            pulseObject.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            var targetFx = pulseObject.GetComponent<HealTargetFx>();
            mannequin.AddComponent<HealDemoRelay>().Bind(zone, targetFx);

            var loop = root.AddComponent<HealFxLoopDemo>();
            loop.Bind(zone);
            var panel = root.AddComponent<HealFxDemoPanel>();
            panel.Bind(zone, loop, new[] { targetFx }, new[] { allyPalette, enemyPalette });

            SavePrefab(root, TrainingRoomPrefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[HealFx] Prefab " + TrainingRoomPrefab + " créé dans " + PrefabsDir);
        }

        /// <summary>Pose le prefab TrainingRoom_HealFx dans la scène ouverte (sous "parent" si fourni) et le sélectionne.</summary>
        public static GameObject PlaceTrainingRoom(Vector3 worldPosition, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/" + TrainingRoomPrefab + ".prefab");
            if (prefab == null)
            {
                Debug.LogError("[HealFx] Prefab " + TrainingRoomPrefab + " introuvable : lance Tools > Heal FX > 3 Prefab Training Room.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            if (parent != null) instance.transform.SetParent(parent, true);
            instance.transform.position = worldPosition;
            Undo.RegisterCreatedObjectUndo(instance, "Place TrainingRoom_HealFx");
            Selection.activeGameObject = instance;
            return instance;
        }
        #endregion

        #region Démo
        private static void SpawnDemoGroup(GameObject zonePrefab, GameObject pulsePrefab, Vector3 origin, string label)
        {
            var group = new GameObject(label);
            group.transform.position = origin;
            Undo.RegisterCreatedObjectUndo(group, "Heal FX demo");

            var zone = (GameObject)PrefabUtility.InstantiatePrefab(zonePrefab, group.transform);
            zone.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            zone.GetComponent<HealZoneFx>().SetAutoPulse(true);

            const int unitCount = 3;
            for (int i = 0; i < unitCount; i++)
            {
                float angle = i / (float)unitCount * Mathf.PI * 2f + 0.5f;
                float distance = 1.1f + (i % 2) * 0.6f;

                GameObject unit = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                unit.name = "Unité factice " + (i + 1);
                unit.transform.SetParent(group.transform, false);
                unit.transform.localPosition = new Vector3(Mathf.Cos(angle) * distance, 1f, Mathf.Sin(angle) * distance);

                // Le pivot de l'unité est au centre de la capsule : on abaisse le FX aux pieds (y = -1).
                var pulse = (GameObject)PrefabUtility.InstantiatePrefab(pulsePrefab, unit.transform);
                pulse.transform.localPosition = new Vector3(0f, -1f + 0.04f, 0f);

                // Abonnement démo : chaque impulsion de zone déclenche l'impulsion de cible.
                unit.AddComponent<HealDemoRelay>().Bind(zone.GetComponent<HealZoneFx>(), pulse.GetComponent<HealTargetFx>());
            }
        }

        private static void AddPrimitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
        }
        #endregion

        #region Matériaux / palettes / meshes
        // Crée le matériau s'il manque ; branche (ou rebranche) la texture si elle est précisée.
        private static Material LoadOrCreateMaterial(string name, Shader shader, string textureProperty, string texturePath)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            if (!string.IsNullOrEmpty(textureProperty) && material.GetTexture(textureProperty) == null)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null) Debug.LogWarning("[HealFx] Texture manquante : " + texturePath);
                material.SetTexture(textureProperty, texture);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static HealFxPalette LoadOrCreatePalette(string name, Color primary, Color secondary, Color core, Color light)
        {
            string path = PalettesDir + "/" + name + ".asset";
            var palette = AssetDatabase.LoadAssetAtPath<HealFxPalette>(path);
            if (palette != null) return palette;

            palette = ScriptableObject.CreateInstance<HealFxPalette>();
            palette.Set(primary, secondary);
            palette.SetRamp(core, light);
            AssetDatabase.CreateAsset(palette, path);
            return palette;
        }

        // Quad XZ unité (-0.5..0.5), UV 0..1 : le shader en déduit p dans [-1,1].
        private static Mesh LoadOrCreateQuadMesh()
        {
            string path = MeshesDir + "/HealZoneQuad.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;

            mesh = new Mesh { name = "HealZoneQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f)
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // Tube ouvert de rayon 1 et de hauteur 1 (base en y = 0) pour la colonne de lumière ; u = tour, v = hauteur.
        private static Mesh LoadOrCreateColumnMesh()
        {
            string path = MeshesDir + "/HealColumnTube.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;

            const int segments = 32;
            var vertices = new Vector3[(segments + 1) * 2];
            var normals = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[(segments + 1) * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction;
                vertices[i * 2 + 1] = direction + Vector3.up;
                normals[i * 2] = direction;
                normals[i * 2 + 1] = direction;
                uvs[i * 2] = new Vector2(i / (float)segments, 0f);
                uvs[i * 2 + 1] = new Vector2(i / (float)segments, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                int t = i * 6;
                triangles[t] = a; triangles[t + 1] = a + 1; triangles[t + 2] = a + 2;
                triangles[t + 3] = a + 2; triangles[t + 4] = a + 1; triangles[t + 5] = a + 3;
            }

            mesh = new Mesh { name = "HealColumnTube", vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        #endregion

        #region Prefab : zone
        private static void BuildZonePrefab(string name, HealFxPalette palette, Mesh quad, Mesh columnMesh,
            Material groundMat, Material columnMat, Material crossMat, Material sparkleMat, Material smokeMat, Material spiralMat)
        {
            var root = new GameObject(name);

            // (1) sol : disque, anneaux, runes
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform, false);
            ground.AddComponent<MeshFilter>().sharedMesh = quad;
            var groundRenderer = ground.AddComponent<MeshRenderer>();
            groundRenderer.sharedMaterial = groundMat;
            groundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            groundRenderer.receiveShadows = false;

            // (2) colonne de lumière
            var column = new GameObject("Column");
            column.transform.SetParent(root.transform, false);
            column.AddComponent<MeshFilter>().sharedMesh = columnMesh;
            var columnRenderer = column.AddComponent<MeshRenderer>();
            columnRenderer.sharedMaterial = columnMat;
            columnRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            columnRenderer.receiveShadows = false;

            // Ambiance : croix qui montent depuis toute la surface (conservées de la v1) + étincelles qui scintillent.
            ParticleSystem crosses = CreateAmbientSystem("Crosses", root.transform, crossMat, 1.6f, 2.4f, 0.7f, 1.3f, 0.20f, 0.30f);
            ParticleSystem sparkles = CreateAmbientSystem("Sparkles", root.transform, sparkleMat, 1.2f, 1.8f, 0.4f, 0.9f, 0.10f, 0.17f);
            ApplyTwinkle(sparkles);

            // (3) volutes montantes (flipbook 8x8, érosion) et (4) fines particules en spirale
            ParticleSystem volutes = CreateVoluteSystem("Volutes", root.transform, smokeMat);
            ParticleSystem spiral = CreateSpiralSystem("Spiral", root.transform, spiralMat);

            // (6) lueur ambiante
            var lightObject = new GameObject("GlowLight");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Light glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 6f;
            glow.intensity = 3.5f;
            glow.shadows = LightShadows.None;

            var fx = root.AddComponent<HealZoneFx>();
            fx.SetReferences(palette, groundRenderer, crosses, sparkles);
            fx.SetLayerReferences(columnRenderer, volutes, spiral, glow);

            SavePrefab(root, name);
        }

        // Particules émises dans un disque au sol, qui montent (vitesse Y locale) puis s'estompent.
        private static ParticleSystem CreateAmbientSystem(string name, Transform parent, Material material,
            float lifeMin, float lifeMax, float riseMin, float riseMax, float sizeMin, float sizeMax)
        {
            ParticleSystem system = CreateSystem(name, parent, material);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.maxParticles = 600;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 10f;

            // Cercle posé à plat (rotation -90° en X), remplissage complet du disque.
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 3f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // IMPORTANT : les 3 axes doivent être dans le même mode (TwoConstants), sinon Unity lève une erreur.
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(riseMin, riseMax);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ApplyFadeAndSize(system);
            return system;
        }

        // Volutes : grandes billboards au flipbook 8x8 qui montent doucement, grossissent, tournent à peine et s'érodent avec l'âge.
        private static ParticleSystem CreateVoluteSystem(string name, Transform parent, Material material)
        {
            ParticleSystem system = CreateSystem(name, parent, material);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 160;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 5f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 2.4f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            // Flipbook 8x8 : l'image avance sur toute la vie de la particule.
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 8;
            sheet.numTilesY = 8;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.cycleCount = 1;

            // Alpha : naît, s'épaissit, puis se déchire (le shader érode d'autant plus que l'alpha est bas).
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(0.25f, 0.55f);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.25f)));
            return system;
        }

        // Fines particules : nées sur un anneau, elles s'élèvent en tournant autour de l'axe et se resserrent légèrement.
        private static ParticleSystem CreateSpiralSystem(string name, Transform parent, Material material)
        {
            ParticleSystem system = CreateSystem(name, parent, material);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.maxParticles = 500;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 30f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 2.4f;
            shape.radiusThickness = 0.35f;     // surtout vers le bord : la spirale part de l'extérieur
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // IMPORTANT : tous les axes (linéaires, orbitaux, radial) dans le même mode (TwoConstants).
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(1.0f, 1.6f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.radial = new ParticleSystem.MinMaxCurve(-0.35f, -0.15f);

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(0.1f, 0.6f);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));
            return system;
        }

        // Scintillement : l'alpha oscille plusieurs fois pendant la vie de l'étincelle.
        private static void ApplyTwinkle(ParticleSystem system)
        {
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.25f, 0.28f),
                    new GradientAlphaKey(1f, 0.42f), new GradientAlphaKey(0.2f, 0.58f), new GradientAlphaKey(0.95f, 0.72f),
                    new GradientAlphaKey(0.35f, 0.86f), new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }
        #endregion

        #region Prefab : cible
        private static void BuildTargetPrefab(string name, HealFxPalette palette, Material ringMat, Material crossMat, Material sparkleMat, Material moteMat)
        {
            var root = new GameObject(name);

            // Anneau : une seule particule à plat qui s'élargit en s'estompant.
            ParticleSystem ring = CreateSystem("Ring", root.transform, ringMat);
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ParticleSystem.MainModule ringMain = ring.main;
            ringMain.loop = true;
            ringMain.playOnAwake = true;
            ringMain.simulationSpace = ParticleSystemSimulationSpace.World;
            ringMain.startLifetime = 0.7f;
            ringMain.startSpeed = 0f;
            ringMain.startSize = 2.2f;
            ringMain.maxParticles = 8;
            ParticleSystem.EmissionModule ringEmission = ring.emission;
            ringEmission.rateOverTime = 0f;                    // émission uniquement par HealTargetFx.Pulse()
            ParticleSystem.ShapeModule ringShape = ring.shape;
            ringShape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule ringSize = ring.sizeOverLifetime;
            ringSize.enabled = true;
            ringSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1f));
            ParticleSystem.ColorOverLifetimeModule ringColor = ring.colorOverLifetime;
            ringColor.enabled = true;
            ringColor.color = FadeGradient(0.05f, 0.25f);

            // Croix et étincelles : petites gerbes vers le haut, émises en rafale par Pulse().
            ParticleSystem crosses = CreateBurstSystem("Crosses", root.transform, crossMat, 1.1f, 1.6f, 0.7f, 1.3f, 0.4f, 0.6f);
            ParticleSystem sparkles = CreateBurstSystem("Sparkles", root.transform, sparkleMat, 0.7f, 1.1f, 0.5f, 1.4f, 0.12f, 0.18f);
            ApplyTwinkle(sparkles);

            // Lueurs : fines particules qui s élèvent en spirale autour du corps pendant le soin (émission pilotée par BeginHeal / EndHeal).
            ParticleSystem motes = CreateMoteSystem("Motes", root.transform, moteMat);

            var fx = root.AddComponent<HealTargetFx>();
            fx.SetReferences(palette, ring, crosses, sparkles);
            fx.SetMoteReference(motes);

            SavePrefab(root, name);
        }

        // Lueurs autour de la cible : nées sur un anneau serré, elles montent en tournant autour du corps, petites et douces.
        private static ParticleSystem CreateMoteSystem(string name, Transform parent, Material material)
        {
            ParticleSystem system = CreateSystem(name, parent, material);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.maxParticles = 120;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;                    // piloté par HealTargetFx

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.5f;
            shape.radiusThickness = 0.25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // IMPORTANT : tous les axes (linéaires, orbitaux) dans le même mode (TwoConstants).
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(0.15f, 0.6f);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));
            return system;
        }

        private static ParticleSystem CreateBurstSystem(string name, Transform parent, Material material,
            float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax)
        {
            ParticleSystem system = CreateSystem(name, parent, material);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.maxParticles = 64;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;

            // Cône tourné vers le haut (rotation -90° en X : l'axe Z du cône devient +Y).
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ApplyFadeAndSize(system);
            return system;
        }
        #endregion

        #region Briques communes
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

        private static void ApplyFadeAndSize(ParticleSystem system)
        {
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(0.15f, 0.55f);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.7f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.8f)));
        }

        // Alpha : 0 -> 1 (fade in) -> maintenu -> 0 (fade out). La couleur vient de startColor.
        private static ParticleSystem.MinMaxGradient FadeGradient(float fadeInEnd, float fadeOutStart)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd),
                    new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void SavePrefab(GameObject root, string name)
        {
            string path = PrefabsDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }
        #endregion
    }
}
