using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DustVFX.Demo
{
    /// <summary>
    /// Scripted capture bench: replays walk / run / sprint / land / turn / stop / jump on any zone,
    /// from any view and light, and writes 1920x1080 PNGs. Drive it with StartShots("sprint:earth:tp:graze;...").
    /// </summary>
    [AddComponentMenu("Dust VFX/Demo Runner")]
    public sealed class DustDemoRunner : MonoBehaviour
    {
        [Header("Scene")]
        public DustDemoDriver driver;
        public DustDemoCamera followCam;
        public Camera captureCamera;
        public Light sun;
        public FootstepDust footsteps;
        public string[] zoneNames = { "earth", "sand", "stone", "snow", "grass", "wood", "water", "slope" };
        public float zoneSpacing = 12f;

        [Header("Output")]
        public string outputDir = "Assets/DustVFX/Captures";
        public int width = 1920;
        public int height = 1080;

        public bool Busy { get; private set; }
        public int ShotsDone { get; private set; }
        public int ShotsTotal { get; private set; }
        public string LastError { get; private set; }
        public readonly List<string> Saved = new List<string>();

        float landedAt = -1f;

        void OnEnable()
        {
            if (footsteps != null) footsteps.DustRequested += OnDust;
        }

        void OnDisable()
        {
            if (footsteps != null) footsteps.DustRequested -= OnDust;
        }

        void OnDust(DustRequest r)
        {
            if (r.kind == DustEventKind.Land) landedAt = Time.time;
        }

        /// <summary>spec = "scenario:zone:view:light[:extraDelay]" entries separated by ';'. First arg = sub folder.</summary>
        public void StartShots(string folder, string spec)
        {
            if (Busy) return;
            StartCoroutine(RunAll(folder, spec));
        }

        IEnumerator RunAll(string folder, string spec)
        {
            Busy = true;
            LastError = null;
            Saved.Clear();
            string[] shots = spec.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            ShotsTotal = shots.Length;
            ShotsDone = 0;
            string dir = Path.Combine(outputDir, folder);
            Directory.CreateDirectory(dir);

            for (int i = 0; i < shots.Length; i++)
            {
                string[] p = shots[i].Trim().Split(':');
                if (p.Length < 4)
                {
                    LastError = "bad spec " + shots[i];
                    continue;
                }
                yield return RunShot(dir, p[0], p[1], p[2], p[3], p.Length > 4 ? float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture) : 0f);
                ShotsDone++;
            }

            driver.commandSpeed = 0f;
            Busy = false;
        }

        IEnumerator RunShot(string dir, string scenario, string zone, string view, string light, float extra)
        {
            int zi = System.Array.IndexOf(zoneNames, zone);
            if (zi < 0) { LastError = "unknown zone " + zone; yield break; }

            ApplyLight(light);
            followCam.view = view == "side" ? DustDemoCamera.View.Side : view == "low" ? DustDemoCamera.View.Low : DustDemoCamera.View.ThirdPerson;

            float cx = zi * zoneSpacing;
            float groundY = zone == "slope" ? 0.5f : 0.02f;
            driver.manual = false;
            driver.commandDir = Vector3.right;
            driver.commandSpeed = 0f;

            float startX = scenario == "walk" ? cx - 2.6f : scenario == "land" ? cx - 0.5f : cx - 5.2f;
            float startY = scenario == "land" ? 3.4f : groundY;
            driver.Teleport(new Vector3(startX, startY, 0f), 90f);
            followCam.SnapTo(driver.transform.position);

            // Let pooled/instantiated state settle, drop any leftover dust from the previous shot.
            DustPool.ClearAll();
            yield return new WaitForSeconds(0.35f);
            if (scenario != "land") { /* standing: nothing to do */ }

            float captureDelay = 1.3f;
            switch (scenario)
            {
                case "walk":
                    driver.commandSpeed = driver.walkSpeed;
                    captureDelay = 2.3f;
                    yield return new WaitForSeconds(captureDelay);
                    break;
                case "run":
                    driver.commandSpeed = driver.runSpeed;
                    captureDelay = 1.6f;
                    yield return new WaitForSeconds(captureDelay);
                    break;
                case "sprint":
                    driver.commandSpeed = driver.sprintSpeed;
                    captureDelay = 1.35f;
                    yield return new WaitForSeconds(captureDelay);
                    break;
                case "land":
                    landedAt = -1f;
                    float t0 = Time.time;
                    while (landedAt < 0f && Time.time - t0 < 4f) yield return null;
                    yield return new WaitForSeconds(0.3f);
                    break;
                case "jump":
                    driver.commandSpeed = driver.runSpeed;
                    yield return new WaitForSeconds(0.8f);
                    landedAt = -1f;
                    driver.Jump();
                    t0 = Time.time;
                    while (landedAt < 0f && Time.time - t0 < 3f) yield return null;
                    yield return new WaitForSeconds(0.3f);
                    break;
                case "turn":
                    driver.commandSpeed = driver.sprintSpeed;
                    yield return new WaitForSeconds(1.0f);
                    driver.commandDir = Vector3.left;
                    yield return new WaitForSeconds(0.55f);
                    break;
                case "stop":
                    driver.commandSpeed = driver.sprintSpeed;
                    yield return new WaitForSeconds(1.1f);
                    driver.commandSpeed = 0f;
                    yield return new WaitForSeconds(0.3f);
                    break;
                default:
                    LastError = "unknown scenario " + scenario;
                    yield break;
            }

            string baseName = $"{scenario}_{zone}_{view}_{light}";
            yield return Capture(Path.Combine(dir, baseName + ".png"));

            if (extra > 0f)
            {
                yield return new WaitForSeconds(extra);
                yield return Capture(Path.Combine(dir, baseName + "_late.png"));
            }
        }

        void ApplyLight(string mode)
        {
            if (sun == null) return;
            if (mode == "sun")
            {
                sun.transform.rotation = Quaternion.Euler(62f, 150f, 0f);
                sun.color = new Color(1f, 0.96f, 0.9f);
                sun.intensity = 2.1f;
            }
            else if (mode == "back")
            {
                sun.transform.rotation = Quaternion.Euler(14f, 270f, 0f);
                sun.color = new Color(1f, 0.82f, 0.62f);
                sun.intensity = 2.8f;
            }
            else
            {
                sun.transform.rotation = Quaternion.Euler(16f, 140f, 0f);
                sun.color = new Color(1f, 0.8f, 0.58f);
                sun.intensity = 3.2f;
            }
        }

        IEnumerator Capture(string path)
        {
            yield return new WaitForEndOfFrame();
            Camera cam = captureCamera != null ? captureCamera : Camera.main;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevTarget;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Saved.Add(path);
        }
    }
}
