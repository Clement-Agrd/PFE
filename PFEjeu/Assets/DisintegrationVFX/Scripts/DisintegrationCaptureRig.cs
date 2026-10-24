#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

namespace Core.DisintegrationVFX
{
    /// <summary>
    /// Outil de capture (Play Mode éditeur) : déclenche une désintégration et sauvegarde des PNG 1920x1080
    /// aux instants demandés, bloom/post-process inclus.
    /// </summary>
    public sealed class DisintegrationCaptureRig : MonoBehaviour
    {
        public static readonly float[] DefaultTimes = { 0.1f, 0.4f, 0.8f, 1.2f, 1.7f, 2.2f, 3.5f };

        public static void Begin(string iteration, string targetName, string view, string styleName = "")
        {
            BeginTimes(iteration, targetName, view, styleName, DefaultTimes);
        }

        public static void BeginTimes(string iteration, string targetName, string view, string styleName, float[] times)
        {
            var go = new GameObject("DisintegrationCaptureRig");
            var rig = go.AddComponent<DisintegrationCaptureRig>();
            rig.StartCoroutine(rig.Run(iteration, targetName, view, styleName, times));
        }

        private IEnumerator Run(string iteration, string targetName, string view, string styleName, float[] times)
        {
            DisintegrationController target = null;
            foreach (var c in FindObjectsByType<DisintegrationController>(FindObjectsInactive.Include))
                if (c.name.Contains(targetName)) { target = c; break; }
            if (target == null) { Debug.LogError("[Capture] cible introuvable : " + targetName); Destroy(gameObject); yield break; }

            Camera main = Camera.main;
            var camGo = Instantiate(main.gameObject);
            camGo.name = "CaptureCam";
            foreach (var mb in camGo.GetComponentsInChildren<MonoBehaviour>())
                if (!(mb is UnityEngine.Rendering.Universal.UniversalAdditionalCameraData)) Destroy(mb);
            var cam = camGo.GetComponent<Camera>();
            var al = camGo.GetComponent<AudioListener>(); if (al != null) Destroy(al);
            cam.enabled = false;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.stereoTargetEye = StereoTargetEyeMask.None;

            target.Reset();
            Vector3 focus = target.GetComponent<Renderer>().bounds.center;
            Vector3 fwd = target.transform.forward;
            switch (view)
            {
                case "front": cam.transform.position = focus + fwd * 4.2f + Vector3.up * 0.4f; break;
                case "34": cam.transform.position = focus + (Quaternion.Euler(0, 45, 0) * fwd) * 4.4f + Vector3.up * 0.6f; break;
                case "back": cam.transform.position = focus - fwd * 4.2f + Vector3.up * 0.5f; break;
                default: cam.transform.position = main.transform.position; focus = target.GetComponent<Renderer>().bounds.center; break;
            }
            cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position);

            if (!string.IsNullOrEmpty(styleName))
            {
                foreach (var s in Resources.FindObjectsOfTypeAll<DisintegrationStyle>())
                    if (s.name.Contains(styleName)) target.Style = s;
            }

            string dir = "Assets/DisintegrationVFX/Captures/" + iteration;
            Directory.CreateDirectory(dir);

            Time.captureDeltaTime = 1f / 30f;
            float t0 = Time.time;
            target.Disintegrate();
            int i = 0;
            while (i < times.Length)
            {
                yield return new WaitForEndOfFrame();
                if (Time.time - t0 >= times[i])
                {
                    cam.Render();
                    Save(rt, dir + "/" + targetName + "_" + view + "_t" + times[i].ToString("0.0") + ".png");
                    i++;
                }
            }

            Time.captureDeltaTime = 0f;
            Destroy(camGo);
            rt.Release();
            Debug.Log("[Capture] done " + iteration + " " + targetName + " " + view);
            Destroy(gameObject);
        }

        private static void Save(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            // la sortie est HDR linéaire -> tonemap/gamma déjà appliqués par URP si post-process actif
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = prev;
            Destroy(tex);
        }
    }
}
#endif
