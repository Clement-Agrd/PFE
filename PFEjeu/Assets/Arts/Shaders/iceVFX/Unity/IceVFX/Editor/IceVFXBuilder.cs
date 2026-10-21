#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IceVFX.EditorTools
{
    /// Menu : Tools/Ice VFX/Build All
    /// Génère textures procédurales, matériaux URP, meshes (cluster, éclat, pic) et prefabs (projectile + impact).
    public static class IceVFXBuilder
    {
        public const string R = "Assets/Arts/Shaders/iceVFX/Unity/IceVFX";
        static readonly Color Ice = new Color(0.62f, 0.9f, 1f), Deep = new Color(0.18f, 0.5f, 0.95f), White = Color.white;

        // ---------------------------------------------------------------- bruit
        static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }
        static float VN(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx), fy);
        }
        static float FBM(float x, float y, int o = 5) { float a = 0.5f, s = 0; for (int i = 0; i < o; i++) { s += a * VN(x, y); x *= 2.03f; y *= 2.03f; a *= 0.5f; } return s; }
        static float SS(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3 - 2 * x); }
        // bruit périodique (normal map tileable)
        static int Mod(int a, int p) => ((a % p) + p) % p;
        static float PN(float u, float v, int p)
        {
            float x = u * p, y = v * p; int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(Mod(xi, p), Mod(yi, p)), Hash(Mod(xi + 1, p), Mod(yi, p)), fx),
                              Mathf.Lerp(Hash(Mod(xi, p), Mod(yi + 1, p)), Hash(Mod(xi + 1, p), Mod(yi + 1, p)), fx), fy);
        }
        static float PHeight(float u, float v) => 0.5f * PN(u, v, 4) + 0.3f * PN(u, v, 8) + 0.15f * PN(u, v, 16) + 0.08f * PN(u, v, 32);

        /// Écrit un asset en conservant son GUID et ses références : si le fichier existe déjà, on copie dedans
        /// (CreateAsset par-dessus un asset chargé laissait les prefabs avec des références nulles).
        static T Save<T>(T fresh, string path) where T : UnityEngine.Object
        {
            fresh.name = Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(fresh, path); return fresh; }
            EditorUtility.CopySerialized(fresh, existing);
            UnityEngine.Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ---------------------------------------------------------------- textures
        static Texture2D Tex(string name, int n, Func<float, float, Color> f, bool normal = false, bool repeat = false)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f((x + 0.5f) / n, (y + 0.5f) / n);
            t.SetPixels32(px);
            string p = R + "/Textures/" + name + ".png";
            File.WriteAllBytes(p, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(p);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.alphaIsTransparency = !normal;
            ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.mipmapEnabled = true; ti.sRGBTexture = !normal;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        // nuage volumétrique : fbm à domaine déformé, éclairé par une fausse lumière haut-gauche (cuite dans le RGB)
        static float Cloud(float u, float v)
        {
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float wx = FBM(u * 3f + 7.1f, v * 3f + 2.3f, 4), wy = FBM(u * 3f + 1.7f, v * 3f + 9.2f, 4);
            float h = FBM(u * 4.5f + wx * 1.7f, v * 4.5f + wy * 1.7f, 5);
            float fall = 1f - SS(0.3f, 1f, r);
            return Mathf.Clamp01((h * 1.7f - 0.22f) * fall * 1.25f);
        }
        static Texture2D TexPuff() => Tex("T_Puff", 256, (u, v) =>
        {
            float a = Cloud(u, v), e = 0.016f;
            float lit = Mathf.Clamp01(0.62f + (Cloud(u - e, v + e) - Cloud(u + e, v - e)) * 10f);
            return new Color(0.5f + 0.5f * lit, 0.66f + 0.34f * lit, 0.82f + 0.18f * lit, a);
        });
        static Texture2D TexGlow() => Tex("T_Glow", 128, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            return new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - r), 2.6f));
        });
        static Texture2D TexSpark() => Tex("T_Spark", 128, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float a = Mathf.Exp(-r * r * 40f)
                    + 0.5f * Mathf.Exp(-Mathf.Abs(dx) * 70f) * Mathf.Exp(-Mathf.Abs(dy) * 7f)
                    + 0.5f * Mathf.Exp(-Mathf.Abs(dy) * 70f) * Mathf.Exp(-Mathf.Abs(dx) * 7f);
            return new Color(1, 1, 1, Mathf.Clamp01(a) * Mathf.Clamp01(1.2f - r));
        });
        static Texture2D TexShard() => Tex("T_Shard", 128, (u, v) =>
        {
            float hw = v < 0.3f ? v / 0.3f * 0.18f : (1 - v) / 0.7f * 0.18f;
            float dx = Mathf.Abs(u - 0.5f);
            float edge = Mathf.Clamp01((hw - dx) * 45f);
            float s = (u < 0.5f ? 1f : 0.68f) + 0.18f * FBM(u * 9f, v * 9f);
            return new Color(Mathf.Clamp01(0.75f * s + 0.2f), Mathf.Clamp01(0.9f * s + 0.08f), 1f, edge * 0.92f);
        });
        static Texture2D TexRing() => Tex("T_Ring", 256, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float a = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.07f, 2f));
            a += 0.18f * Mathf.Exp(-Mathf.Pow((r - 0.55f) / 0.3f, 2f)) * FBM(u * 8f, v * 8f);
            a *= 0.65f + 0.7f * FBM(u * 14f, v * 14f);
            return new Color(1, 1, 1, Mathf.Clamp01(a) * Mathf.Clamp01((1f - r) * 8f + 0.2f));
        });
        // masque d'anneau doux pour la distorsion (pas de bruit : la normal map fait le travail)
        static Texture2D TexRingSoft() => Tex("T_RingSoft", 256, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float a = Mathf.Exp(-Mathf.Pow((r - 0.72f) / 0.16f, 2f)) * Mathf.Clamp01((1f - r) * 6f);
            return new Color(1, 1, 1, a);
        });
        static Texture2D TexFrost() => Tex("T_Frost", 512, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f, ang = Mathf.Atan2(dy, dx);
            float spokes = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f)), 28f) * (1 - r);
            float sub = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 12f + r * 14f)), 40f) * 0.55f * (1 - r) * (1 - r);
            float patch = Mathf.Clamp01((0.92f - r) * 3f) * Mathf.Clamp01(FBM(u * 7f, v * 7f) * 1.7f - 0.4f);
            float fine = Mathf.Clamp01(FBM(u * 40f, v * 40f) - 0.55f) * Mathf.Clamp01(1.0f - r) * 1.5f;
            float a = Mathf.Clamp01(spokes + sub + patch * 0.85f + fine * 0.5f) * Mathf.Clamp01((0.98f - r) * 10f);
            return new Color(0.85f, 0.97f, 1f, a);
        });
        static Texture2D TexStreak() => Tex("T_Streak", 64, (u, v) =>
            new Color(1, 1, 1, Mathf.Pow(1f - Mathf.Abs(2f * v - 1f), 1.6f) * SS(0f, 0.12f, u)));
        static Texture2D TexNormal() => Tex("T_IceNormal", 256, (u, v) =>
        {
            float e = 1f / 256f;
            float dhu = (PHeight(u + e, v) - PHeight(u - e, v)) / (2f * e);
            float dhv = (PHeight(u, v + e) - PHeight(u, v - e)) / (2f * e);
            var n = new Vector3(-dhu * 0.35f, -dhv * 0.35f, 1f).normalized;
            return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }, true, true);

        // ---------------------------------------------------------------- matériaux
        static Material Particle(string name, Texture2D t, bool additive, Color c, float intensity, bool soft = true)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a));
            if (soft) { m.SetFloat("_SoftParticlesEnabled", 1); m.EnableKeyword("_SOFTPARTICLES_ON"); }
            else m.SetFloat("_Cull", 0);   // quads posés à plat : visibles des deux faces (le Quad regarde -Z)
            if (soft) { m.SetFloat("_SoftParticlesNearFadeDistance", 0f); m.SetFloat("_SoftParticlesFarFadeDistance", 0.8f); }
            m = Save(m, R + "/Materials/" + name + ".mat");
            return m;
        }
        /// Estompe les particules proches de la caméra (la traînée passe à côté du joueur sans l'aveugler).
        static Material CamFade(Material m)
        {
            m.SetFloat("_CameraFadingEnabled", 1); m.EnableKeyword("_FADING_ON");
            m.SetFloat("_CameraNearFadeDistance", 1.2f); m.SetFloat("_CameraFarFadeDistance", 3.5f);
            EditorUtility.SetDirty(m);
            return m;
        }
        static Material Distortion(string name, Texture2D mask, Texture2D bump, float strength, float radial, float tiling)
        {
            var m = new Material(Shader.Find("IceVFX/IceDistortion"));
            m.SetTexture("_MainTex", mask); m.SetTexture("_BumpMap", bump);
            m.SetFloat("_Strength", strength); m.SetFloat("_Radial", radial); m.SetFloat("_Tiling", tiling);
            m.renderQueue = 2950;
            m = Save(m, R + "/Materials/" + name + ".mat");
            return m;
        }
        static Material Glass()
        {
            var iceShader = Shader.Find("IceVFX/IceCrystal");
            if (iceShader != null)
            {
                var mi = new Material(iceShader);
                mi = Save(mi, R + "/Materials/M_IceGlass.mat");
                return mi;
            }
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0); m.SetFloat("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
            m.SetColor("_BaseColor", new Color(0.62f, 0.9f, 1f, 0.42f));
            m.SetFloat("_Smoothness", 0.97f); m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.12f, 0.5f, 0.8f));
            m = Save(m, R + "/Materials/M_IceGlass.mat");
            return m;
        }

        // ---------------------------------------------------------------- decal (optionnel)
        static bool DecalFeatureActive()
        {
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null) rp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (rp == null) return false;
            var list = new SerializedObject(rp).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                var rd = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                if (rd == null) continue;
                foreach (var f in rd.rendererFeatures) if (f is DecalRendererFeature && f.isActive) return true;
            }
            return false;
        }
        static Material DecalMaterial(Texture2D tex)
        {
            var sh = Shader.Find("Shader Graphs/Decal");
            if (!sh) return null;
            var m = new Material(sh);
            for (int i = 0; i < sh.GetPropertyCount(); i++)
                if (sh.GetPropertyType(i) == ShaderPropertyType.Texture && sh.GetPropertyName(i).Contains("Base")) { m.SetTexture(sh.GetPropertyName(i), tex); break; }
            m = Save(m, R + "/Materials/M_FrostDecal.mat");
            return m;
        }

        // ---------------------------------------------------------------- meshes cristal
        // mode : 0 = grappe héros, 1 = éclat simple (débris), 2 = pic (long axe +Y, base à l'origine)
        // COLOR.rgb = coordonnées barycentriques (arêtes lumineuses) ; la diagonale des quads de facette est désactivée.
        static Mesh Crystals(int mode)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var col = new List<Color>(); var rnd = new System.Random(11);
            Func<float> Rn = () => (float)rnd.NextDouble();
            var cn = new[] { new Color(1, 0, 0, 1), new Color(0, 1, 0, 1), new Color(0, 0, 1, 1) };
            var noR = new[] { new Color(1, 0, 0, 1), new Color(1, 1, 0, 1), new Color(1, 0, 1, 1) };   // arête opposée au sommet 0 masquée
            var noG = new[] { new Color(1, 1, 0, 1), new Color(0, 1, 0, 1), new Color(0, 1, 1, 1) };   // arête opposée au sommet 1 masquée
            Action<Vector3, Vector3, Vector3, Color[]> Tri = (a, b, c, cc) =>
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2);
                col.Add(cc[0]); col.Add(cc[1]); col.Add(cc[2]);
            };
            Action<Vector3, Vector3, float, float, float> Crystal = (pos, dir, r, h, shoulder) =>
            {
                dir.Normalize();
                Vector3 up = Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up;
                Vector3 s = Vector3.Cross(dir, up).normalized, u = Vector3.Cross(dir, s);
                var b = new Vector3[6]; var m = new Vector3[6];
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f + Rn() * 0.12f; Vector3 rad = s * Mathf.Cos(a) + u * Mathf.Sin(a);
                    b[i] = pos + rad * r * (0.7f + Rn() * 0.1f);
                    m[i] = pos + dir * h * (shoulder + Rn() * 0.08f) + rad * r * (0.95f + Rn() * 0.15f);
                }
                Vector3 tip = pos + dir * h + s * (Rn() - 0.5f) * r * 0.35f + u * (Rn() - 0.5f) * r * 0.35f;
                for (int i = 0; i < 6; i++)
                {
                    int j = (i + 1) % 6;
                    Tri(b[i], b[j], m[i], noR); Tri(b[j], m[j], m[i], noG); Tri(m[i], m[j], tip, cn);
                }
            };
            if (mode == 1) Crystal(new Vector3(0, 0, -0.5f), Vector3.forward, 0.45f, 1f, 0.68f);
            else if (mode == 2) Crystal(Vector3.zero, Vector3.up, 0.5f, 1f, 0.38f);   // épaule basse : long fût effilé
            else
            {
                Crystal(new Vector3(0, 0, -0.15f), Vector3.forward, 0.12f, 0.95f, 0.68f);
                for (int i = 0; i < 16; i++)
                {
                    float k = i / 16f, a = i * 2.39996f;
                    Vector3 d = new Vector3(Mathf.Cos(a) * (0.5f + k), Mathf.Sin(a) * (0.5f + k), 0.9f - k * 1.6f);
                    Crystal(d.normalized * 0.04f, d, 0.045f + Rn() * 0.04f, 0.26f + Rn() * 0.36f - k * 0.08f, 0.68f);
                }
            }
            var mesh = new Mesh { name = mode == 1 ? "IceShardSingle" : mode == 2 ? "IceSpike" : "IceCrystalCluster" };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.SetColors(col); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh = Save(mesh, R + "/Meshes/" + mesh.name + ".asset");
            return mesh;
        }

        // ---------------------------------------------------------------- helpers particules
        static ParticleSystem PS(Transform parent, string name, Material mat, bool world = false, float dur = 5f, bool loop = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat; rend.shadowCastingMode = ShadowCastingMode.Off; rend.receiveShadows = false;
            var m = ps.main; m.duration = dur; m.loop = loop; m.playOnAwake = true; m.startSpeed = 0f;
            m.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            var sh = ps.shape; sh.enabled = false;
            var em = ps.emission; em.rateOverTime = 0f;
            return ps;
        }
        static ParticleSystem.MinMaxCurve C(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);
        static void Life(ParticleSystem ps, float a, float b) { var m = ps.main; m.startLifetime = C(a, b); }
        static void Size(ParticleSystem ps, float a, float b) { var m = ps.main; m.startSize = C(a, b); }
        static void Speed(ParticleSystem ps, float a, float b) { var m = ps.main; m.startSpeed = C(a, b); }
        static void Rot(ParticleSystem ps) { var m = ps.main; m.startRotation = C(0f, 6.283f); }
        static void Spin(ParticleSystem ps, float s) { var r = ps.rotationOverLifetime; r.enabled = true; r.z = C(-s, s); }
        static void Burst(ParticleSystem ps, int n, float t = 0f) { var e = ps.emission; e.SetBursts(new[] { new ParticleSystem.Burst(t, (short)n) }); }
        static void Grow(ParticleSystem ps, float s0, float s1)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, s0), new Keyframe(0.3f, s0 + (s1 - s0) * 0.78f), new Keyframe(1, s1)));
        }
        static void Colors(ParticleSystem ps, Color c0, Color c1, float a, float fadeIn = 0.15f, float hold = 0.5f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c0, 0), new GradientColorKey(c1, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(a, fadeIn), new GradientAlphaKey(a * 0.7f, hold), new GradientAlphaKey(0, 1) });
            var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g);
        }
        static void Noise(ParticleSystem ps, float str, float freq, int octaves = 1)
        {
            var n = ps.noise; n.enabled = true; n.strength = str; n.frequency = freq; n.scrollSpeed = 0.3f; n.octaveCount = octaves; n.damping = true;
        }
        static void Hemi(ParticleSystem ps, float radius) { var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Hemisphere; s.radius = radius; }
        static void Flat(ParticleSystem ps, Mesh quad)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = quad; r.alignment = ParticleSystemRenderSpace.Local;
        }
        /// Trails fins et lumineux sur les étincelles.
        static void SparkTrails(ParticleSystem ps, Material trailMat, float life)
        {
            var t = ps.trails; t.enabled = true; t.mode = ParticleSystemTrailMode.PerParticle; t.ratio = 1f;
            t.lifetime = new ParticleSystem.MinMaxCurve(life); t.minVertexDistance = 0.03f; t.worldSpace = true; t.dieWithParticles = true;
            t.textureMode = ParticleSystemTrailTextureMode.Stretch; t.sizeAffectsWidth = true; t.sizeAffectsLifetime = false; t.inheritParticleColor = true;
            t.widthOverTrail = new ParticleSystem.MinMaxCurve(0.9f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0)));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(new Color(0.35f, 0.75f, 1f), 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            t.colorOverTrail = new ParticleSystem.MinMaxGradient(g);
            ps.GetComponent<ParticleSystemRenderer>().trailMaterial = trailMat;
        }

        // ---------------------------------------------------------------- build
        [MenuItem("Tools/Ice VFX/Build All")]
        public static void BuildAll()
        {
            if (Shader.Find("Universal Render Pipeline/Particles/Unlit") == null) { Debug.LogError("[IceVFX] URP introuvable."); return; }
            if (Shader.Find("IceVFX/IceDistortion") == null) { Debug.LogError("[IceVFX] Shader IceVFX/IceDistortion introuvable (erreur de compilation ?)."); return; }
            foreach (var d in new[] { "Textures", "Materials", "Meshes", "Prefabs" }) Directory.CreateDirectory(R + "/" + d);
            AssetDatabase.Refresh();

            var tPuff = TexPuff(); var tGlow = TexGlow(); var tSpark = TexSpark(); var tShard = TexShard(); var tRing = TexRing(); var tFrost = TexFrost();
            var tRingSoft = TexRingSoft(); var tStreak = TexStreak(); var tNormal = TexNormal();

            var mGlass = Glass();
            var mCore = Particle("M_Core", tGlow, true, new Color(0.75f, 0.96f, 1f), 2.2f);
            var mHalo = Particle("M_Halo", tGlow, true, new Color(0.2f, 0.6f, 1f), 0.9f);
            var mFlash = Particle("M_Flash", tGlow, true, new Color(0.9f, 0.98f, 1f), 6f);
            var mMist = CamFade(Particle("M_Mist", tPuff, false, new Color(0.9f, 0.97f, 1f), 1f));
            var mMistGlow = CamFade(Particle("M_MistGlow", tPuff, true, new Color(0.18f, 0.55f, 1f), 1.2f));
            var mSpark = Particle("M_Spark", tSpark, true, new Color(0.6f, 0.9f, 1f), 4f);
            var mTrail = Particle("M_SparkTrail", tStreak, true, new Color(0.55f, 0.88f, 1f), 5f);
            var mShard = Particle("M_Shard", tShard, false, White, 1.1f);
            var mRing = Particle("M_Ring", tRing, true, new Color(0.7f, 0.93f, 1f), 3f, false);   // posé au sol : pas de soft particles
            var mFrost = Particle("M_Frost", tFrost, false, new Color(0.85f, 0.97f, 1f), 1.15f, false);
            var mDistAir = Distortion("M_DistortAir", tGlow, tNormal, 0.012f, 0f, 1.2f);
            var mDistRing = Distortion("M_DistortRing", tRingSoft, tNormal, 0.055f, 1f, 1.6f);

            var cluster = Crystals(0); var single = Crystals(1); var spike = Crystals(2);
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            bool useDecal = DecalFeatureActive();
            var decalMat = useDecal ? DecalMaterial(tFrost) : null;
            if (useDecal && decalMat == null) useDecal = false;
            Debug.Log("[IceVFX] Givre au sol : " + (useDecal ? "Decal Projector (Decal Renderer Feature actif)" : "quad (pas de Decal Renderer Feature actif)"));

            // ============================ IMPACT ============================
            var imp = new GameObject("IceImpact");
            var ic = imp.AddComponent<IceImpact>();
            var lg = new GameObject("Light"); lg.transform.SetParent(imp.transform, false); lg.transform.localPosition = new Vector3(0, 0, 0.5f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.6f, 0.88f, 1f); l.range = 10f; l.intensity = 0f; ic.flashLight = l;

            // t=0 : flash blanc (~0.1 s)
            var flash = PS(imp.transform, "Flash", mFlash, false, 1f, false); Life(flash, 0.12f, 0.12f); Size(flash, 5f, 5f); Burst(flash, 1); Grow(flash, 0.35f, 1.3f);
            Colors(flash, White, Ice, 1f, 0.03f, 0.4f);

            // t=0.05 : onde de choc (anneau lumineux au sol + distorsion)
            var ring = PS(imp.transform, "Shockwave", mRing, false, 1f, false); Flat(ring, quad); Life(ring, 0.45f, 0.45f); Size(ring, 7f, 7f); Burst(ring, 1, 0.05f);
            var rs = ring.sizeOverLifetime; rs.enabled = true; rs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.1f), new Keyframe(0.25f, 0.7f), new Keyframe(1, 1f)));
            Colors(ring, White, Ice, 1f, 0.05f, 0.4f);
            var ring2 = PS(imp.transform, "Shockwave2", mRing, false, 1f, false); Flat(ring2, quad); Life(ring2, 0.6f, 0.6f); Size(ring2, 4.5f, 4.5f); Burst(ring2, 1, 0.1f); Grow(ring2, 0.1f, 1f);
            Colors(ring2, Ice, Deep, 0.7f, 0.05f, 0.4f);
            var dRing = PS(imp.transform, "DistortShock", mDistRing, false, 1f, false); Life(dRing, 0.55f, 0.55f); Size(dRing, 9f, 9f); Burst(dRing, 1, 0.04f);
            Grow(dRing, 0.08f, 1f); Colors(dRing, White, White, 1f, 0.05f, 0.35f);
            dRing.transform.localPosition = new Vector3(0, 0, 0.4f);

            // givre au sol : decal si dispo, sinon quad (apparaît ~0.15 s, tient, puis s'estompe)
            if (useDecal)
            {
                var dg = new GameObject("FrostDecal"); dg.transform.SetParent(imp.transform, false);
                dg.transform.localPosition = new Vector3(0, 0, 0.5f); dg.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                var dp = dg.AddComponent<DecalProjector>(); dp.material = decalMat; dp.size = new Vector3(5f, 5f, 1.5f); dp.fadeFactor = 0f;
                ic.frostDecal = dp;
            }
            else
            {
                var frost = PS(imp.transform, "GroundFrost", mFrost, false, 1f, false); frost.transform.localPosition = new Vector3(0, 0, 0.02f); Flat(frost, quad);
                Life(frost, 4.5f, 4.5f); Size(frost, 4.2f, 4.8f); Burst(frost, 1, 0.12f); Rot(frost);
                var fs = frost.sizeOverLifetime; fs.enabled = true;
                fs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.25f), new Keyframe(0.06f, 0.9f), new Keyframe(1, 1f)));
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(Ice, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.95f, 0.05f), new GradientAlphaKey(0.8f, 0.55f), new GradientAlphaKey(0, 1) });
                var c = frost.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g);
            }

            // t=0 : bris du cristal en éclats physiques (alpha over lifetime => fonte via COLOR.a du shader)
            Gradient dissolveG = new Gradient();
            dissolveG.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(White, 1) },
                              new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            Action<ParticleSystem> debrisCommon = d =>
            {
                var r = d.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = single; r.alignment = ParticleSystemRenderSpace.Local;
                var m = d.main; m.gravityModifier = 1.6f; m.startRotation3D = true; m.startRotationX = C(0, 6.28f); m.startRotationY = C(0, 6.28f); m.startRotationZ = C(0, 6.28f);
                var ro = d.rotationOverLifetime; ro.enabled = true; ro.separateAxes = true; ro.x = C(-8f, 8f); ro.y = C(-8f, 8f); ro.z = C(-8f, 8f);
                var cl = d.colorOverLifetime; cl.enabled = true; cl.color = new ParticleSystem.MinMaxGradient(dissolveG);
                var co = d.collision; co.enabled = true; co.type = ParticleSystemCollisionType.World; co.mode = ParticleSystemCollisionMode.Collision3D; co.bounce = 0.3f; co.dampen = 0.3f; co.lifetimeLoss = 0.1f; co.radiusScale = 0.4f;
            };
            var debris = PS(imp.transform, "CrystalDebris", mGlass, true, 3f, false); Burst(debris, 18); Hemi(debris, 0.15f); debrisCommon(debris);
            Life(debris, 1.2f, 2.2f); Speed(debris, 4f, 10f); Size(debris, 0.1f, 0.26f);
            var debrisBig = PS(imp.transform, "CrystalDebrisBig", mGlass, true, 3f, false); Burst(debrisBig, 5); Hemi(debrisBig, 0.1f); debrisCommon(debrisBig);
            Life(debrisBig, 1.4f, 2.4f); Speed(debrisBig, 1.5f, 5f); Size(debrisBig, 0.3f, 0.55f);

            var shards = PS(imp.transform, "Shards", mShard, true, 2f, false); Burst(shards, 70, 0.02f); Hemi(shards, 0.12f);
            Life(shards, 0.7f, 1.5f); Speed(shards, 4f, 12f); Size(shards, 0.07f, 0.18f); Rot(shards); Spin(shards, 4f);
            { var m = shards.main; m.gravityModifier = 1.2f; var c = shards.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D; c.bounce = 0.3f; c.dampen = 0.3f; c.radiusScale = 0.3f; }
            Colors(shards, White, Ice, 1f, 0.02f, 0.7f);

            var sp = PS(imp.transform, "Sparks", mSpark, true, 2f, false); Burst(sp, 34, 0.03f); Hemi(sp, 0.1f);
            Life(sp, 0.9f, 1.5f); Speed(sp, 1.5f, 6f); Size(sp, 0.05f, 0.12f); Noise(sp, 0.6f, 0.8f);
            { var m = sp.main; m.gravityModifier = 0.4f; }
            Colors(sp, White, new Color(0.3f, 0.7f, 1f), 1f, 0.03f, 0.6f);
            SparkTrails(sp, mTrail, 0.22f);

            // t=0.1 : brume dense qui monte puis retombe, + nappe basse qui s'étale sur la surface
            var mist = PS(imp.transform, "Mist", mMist, true, 3f, false); Burst(mist, 24, 0.1f); Hemi(mist, 0.3f);
            Life(mist, 2f, 3.2f); Speed(mist, 0.6f, 2.4f); Size(mist, 1.2f, 2.2f); Rot(mist); Spin(mist, 0.4f); Noise(mist, 0.45f, 0.4f, 2); Grow(mist, 0.5f, 1.7f);
            { var m = mist.main; m.gravityModifier = 0.1f; var lv = mist.limitVelocityOverLifetime; lv.enabled = true; lv.limit = new ParticleSystem.MinMaxCurve(0.4f); lv.dampen = 0.12f; }
            Colors(mist, new Color(0.92f, 0.98f, 1f), new Color(0.4f, 0.7f, 0.95f), 0.6f, 0.1f, 0.5f);
            var gm = PS(imp.transform, "GroundMist", mMist, true, 3f, false); Burst(gm, 16, 0.15f);
            { var s = gm.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle; s.radius = 0.3f; s.radiusThickness = 0f; }
            Life(gm, 2.4f, 3.6f); Speed(gm, 1.2f, 3f); Size(gm, 1.1f, 1.8f); Rot(gm); Spin(gm, 0.3f); Noise(gm, 0.3f, 0.35f, 2); Grow(gm, 0.5f, 1.5f);
            { var lv = gm.limitVelocityOverLifetime; lv.enabled = true; lv.limit = new ParticleSystem.MinMaxCurve(0.5f); lv.dampen = 0.2f; var m = gm.main; m.gravityModifier = 0.05f; }
            Colors(gm, new Color(0.85f, 0.95f, 1f), new Color(0.35f, 0.65f, 0.95f), 0.5f, 0.12f, 0.5f);

            // t=0.1+ : pics de glace (6..9 tirés au spawn ; scale Y 0->1 en 0.12 s avec overshoot, puis dissolve)
            var spikesRoot = new GameObject("Spikes"); spikesRoot.transform.SetParent(imp.transform, false);
            var spikeList = new Transform[9];
            for (int i = 0; i < 9; i++)
            {
                var sg = new GameObject("Spike_" + i); sg.transform.SetParent(spikesRoot.transform, false);
                sg.AddComponent<MeshFilter>().sharedMesh = spike;
                var sr = sg.AddComponent<MeshRenderer>(); sr.sharedMaterial = mGlass; sr.shadowCastingMode = ShadowCastingMode.Off; sr.receiveShadows = false;
                sg.transform.localScale = Vector3.zero;
                spikeList[i] = sg.transform;
            }
            ic.spikes = spikeList;

            var impPrefab = PrefabUtility.SaveAsPrefabAsset(imp, R + "/Prefabs/IceImpact.prefab", out bool okImp);
            if (!okImp) Debug.LogError("[IceVFX] Échec SaveAsPrefabAsset IceImpact");
            UnityEngine.Object.DestroyImmediate(imp);

            // ============================ PROJECTILE ============================
            var root = new GameObject("IceProjectile");
            var ip = root.AddComponent<IceProjectile>(); ip.impactPrefab = impPrefab;
            var rb = root.GetComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var scol = root.AddComponent<SphereCollider>(); scol.isTrigger = true; scol.radius = 0.3f;

            var cr = new GameObject("Crystal"); cr.transform.SetParent(root.transform, false); cr.transform.localScale = Vector3.one * 1.25f;
            cr.AddComponent<MeshFilter>().sharedMesh = cluster;
            var crr = cr.AddComponent<MeshRenderer>(); crr.sharedMaterial = mGlass; crr.shadowCastingMode = ShadowCastingMode.Off; crr.receiveShadows = false;
            cr.AddComponent<IceSpin>();
            ip.crystal = crr;

            var core = PS(root.transform, "Core", mCore); Life(core, 0.35f, 0.35f); Size(core, 0.3f, 0.45f);
            { var e = core.emission; e.rateOverTime = 14f; } Colors(core, White, Ice, 1f, 0.3f, 0.6f);
            var halo = PS(root.transform, "Halo", mHalo); Life(halo, 0.6f, 0.6f); Size(halo, 1.1f, 1.4f);
            { var e = halo.emission; e.rateOverTime = 8f; } Colors(halo, Ice, Deep, 0.4f, 0.3f, 0.6f);

            // air froid autour de la tête : 1 particule, taille 1.8, distorsion faible
            var air = PS(root.transform, "ColdAir", mDistAir, false, 5f, false); Life(air, 99f, 99f); Size(air, 1.8f, 1.8f); Burst(air, 1);
            { var m = air.main; m.maxParticles = 1; m.startColor = new Color(1, 1, 1, 0.55f); }

            var trails = new GameObject("Trails"); trails.transform.SetParent(root.transform, false); ip.trails = trails.transform;

            // brume dense et volumétrique : rotation, bruit, grossissement ; lueur bleue interne en début de vie
            var tm = PS(trails.transform, "Mist", mMist, true); Life(tm, 1.3f, 2.1f); Size(tm, 0.35f, 0.6f); Speed(tm, 0.1f, 0.4f); Rot(tm); Spin(tm, 0.8f); Noise(tm, 0.55f, 0.5f, 2); Grow(tm, 0.5f, 3f);
            { var e = tm.emission; e.rateOverDistance = 26f; e.rateOverTime = 18f; var s = tm.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 0.1f; }
            Colors(tm, new Color(0.9f, 0.97f, 1f), new Color(0.28f, 0.55f, 0.88f), 0.42f, 0.08f, 0.45f);
            var tg = PS(trails.transform, "MistGlow", mMistGlow, true); Life(tg, 0.3f, 0.5f); Size(tg, 0.4f, 0.7f); Rot(tg); Spin(tg, 0.6f); Grow(tg, 0.6f, 1.6f);
            { var e = tg.emission; e.rateOverDistance = 14f; e.rateOverTime = 8f; var s = tg.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 0.06f; }
            Colors(tg, White, new Color(0.2f, 0.5f, 1f), 0.9f, 0.05f, 0.3f);

            var ts = PS(trails.transform, "Sparks", mSpark, true); Life(ts, 0.5f, 1f); Size(ts, 0.03f, 0.08f); Speed(ts, 0.5f, 2.5f); Noise(ts, 0.5f, 1f);
            { var e = ts.emission; e.rateOverTime = 30f; e.rateOverDistance = 8f; var s = ts.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 20f; s.radius = 0.05f; s.rotation = new Vector3(180, 0, 0); var m = ts.main; m.gravityModifier = 0.1f; }
            Colors(ts, White, new Color(0.4f, 0.8f, 1f), 1f, 0.05f, 0.6f);
            SparkTrails(ts, mTrail, 0.35f);

            var th = PS(trails.transform, "Shards", mShard, true); Life(th, 0.7f, 1.1f); Size(th, 0.06f, 0.14f); Speed(th, 0.2f, 1.2f); Rot(th); Spin(th, 3f);
            { var e = th.emission; e.rateOverDistance = 5f; var s = th.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 0.1f; }
            Colors(th, White, Ice, 1f, 0.05f, 0.6f);

            var pl = new GameObject("Light"); pl.transform.SetParent(root.transform, false);
            var pLight = pl.AddComponent<Light>(); pLight.type = LightType.Point; pLight.color = new Color(0.55f, 0.86f, 1f); pLight.range = 6f; pLight.intensity = 3f; ip.glow = pLight;

            PrefabUtility.SaveAsPrefabAsset(root, R + "/Prefabs/IceProjectile.prefab", out bool okProj);
            if (!okProj) Debug.LogError("[IceVFX] Échec SaveAsPrefabAsset IceProjectile");
            UnityEngine.Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            // les prefabs en cache pointent sur les anciennes instances de matériaux : on force leur réimport
            foreach (var pn in new[] { "IceImpact", "IceProjectile" }) AssetDatabase.ImportAsset(R + "/Prefabs/" + pn + ".prefab", ImportAssetOptions.ForceUpdate);
            Debug.Log("[IceVFX] OK. Prefabs : " + R + "/Prefabs/. Ajoute IceShooter sur la caméra pour tester.");
        }
    }
}
#endif
