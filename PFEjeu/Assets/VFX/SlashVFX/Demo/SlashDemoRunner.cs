using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SlashVFX.Demo
{
    /// <summary>
    /// Capture bench. StartShots(folder, "step:view:time;..."): step 1-4, view front|q3|game|side,
    /// time in seconds since the slash started (the slash is frozen at that time, then rendered 1920x1080).
    /// StartSequence(folder, view) plays 1-2-3-Finisher in real time and captures each hit.
    /// </summary>
    [AddComponentMenu("Slash VFX/Demo Runner")]
    public sealed class SlashDemoRunner : MonoBehaviour
    {
        public SlashComboController controller;
        public Camera cam;
        public Transform pivot;
        public Transform mannequin;
        public string outputDir = "Assets/SlashVFX/Captures";
        public int width = 1920;
        public int height = 1080;

        public bool Busy { get; private set; }
        public int Done { get; private set; }
        public int Total { get; private set; }
        public string LastError { get; private set; }
        public readonly List<string> Saved = new List<string>();

        public void StartShots(string folder, string spec)
        {
            if (!Busy) StartCoroutine(RunShots(folder, spec));
        }

        public void StartSequence(string folder, string view)
        {
            if (!Busy) StartCoroutine(RunSequence(folder, view));
        }

        void SetView(string view)
        {
            Vector3 p = pivot.position;
            Vector3 look = p + new Vector3(0f, -0.1f, 1.2f);
            Vector3 pos;
            switch (view)
            {
                case "front": pos = p + new Vector3(0f, 0.5f, 5.4f); break;
                case "q3": pos = p + new Vector3(3.6f, 1.6f, 3.4f); break;
                case "side": pos = p + new Vector3(5.2f, 0.4f, 1.2f); break;
                default: pos = p + new Vector3(0f, 1.5f, -4.6f); break; // gameplay camera
            }
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        }

        IEnumerator RunShots(string folder, string spec)
        {
            Busy = true; LastError = null; Saved.Clear(); Done = 0;
            string dir = Path.Combine(outputDir, folder);
            Directory.CreateDirectory(dir);
            bool hs = controller.enableHitStop;
            controller.enableHitStop = false;
            controller.readMouse = false;

            string[] shots = spec.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            Total = shots.Length;
            foreach (string sh in shots)
            {
                string[] p = sh.Trim().Split(':');
                int step = int.Parse(p[0]) - 1;
                string view = p[1];
                float t = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);

                SetView(view);
                Time.timeScale = 1f;
                yield return new WaitForSeconds(0.2f);
                controller.PlayStep(step);
                SlashInstance inst = controller.LastInstance;
                Time.timeScale = 0.15f;
                float guard = Time.realtimeSinceStartup + 6f;
                while (inst != null && inst.Age < t && Time.realtimeSinceStartup < guard) yield return null;
                Time.timeScale = 0f;
                yield return new WaitForEndOfFrame();
                Capture(Path.Combine(dir, $"c{step + 1}_{view}_{Mathf.RoundToInt(t * 1000):D3}ms.png"));
                Time.timeScale = 1f;
                if (inst != null) inst.Hide();
                Done++;
            }
            controller.enableHitStop = hs;
            Busy = false;
        }

        IEnumerator RunSequence(string folder, string view)
        {
            Busy = true; LastError = null; Saved.Clear(); Done = 0;
            string dir = Path.Combine(outputDir, folder);
            Directory.CreateDirectory(dir);
            controller.readMouse = false;
            SetView(view);
            Time.timeScale = 1f;
            Total = controller.steps.Length;
            yield return new WaitForSeconds(0.4f);
            for (int i = 0; i < controller.steps.Length; i++)
            {
                controller.RegisterAttack();
                float wait = 0.033f + controller.steps[i].duration * controller.steps[i].impactFraction + 0.045f;
                yield return new WaitForSeconds(wait);
                yield return new WaitForEndOfFrame();
                Capture(Path.Combine(dir, $"seq{i + 1}_{view}.png"));
                Done++;
                yield return new WaitForSeconds(controller.steps[i].duration * 0.8f + 0.05f);
            }
            yield return new WaitForSeconds(1.0f);
            Busy = false;
        }

        void Capture(string path)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevT;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prevA;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Saved.Add(path);
        }
    }
}
