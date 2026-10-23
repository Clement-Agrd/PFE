using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FireVFX
{
    /// Banc de capture déterministe (Play Mode) : avance à pas fixe (Time.captureDeltaTime),
    /// ce qui donne exactement les mêmes images quelle que soit la vitesse de l'éditeur.
    public class FireCaptureRig : MonoBehaviour
    {
        public GameObject projectilePrefab, impactPrefab;
        public string outDir = "FireVFX_Captures";
        public float fps = 30f;
        public Camera cam;
        public string[] hide;
        [System.NonSerialized] public bool busy;
        void OnEnable() { busy = false; }

        /// Rend la caméra (post-process compris) dans une RT 1920x1080 et l'enregistre en PNG.
        void Grab(string path)
        {
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); rt.Create();
            var prev = cam.targetTexture; cam.targetTexture = rt; cam.aspect = 1920f / 1080f; cam.Render(); cam.targetTexture = prev; cam.ResetAspect();
            var old = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt);
        }

        public void Run(string scenario, string tag) { if (!busy) StartCoroutine(Do(scenario, tag)); }

        IEnumerator Do(string scenario, string tag)
        {
            busy = true;
            Directory.CreateDirectory(outDir);
            FireSettings.feedback = false;
            Time.captureDeltaTime = 1f / fps;
            if (!cam) cam = Camera.main;
            yield return null;

            GameObject go = null;
            float[] times;
            Vector3 camPos = Vector3.zero, camLook = Vector3.zero; Transform follow = null; Vector3 followOffset = Vector3.zero;

            switch (scenario)
            {
                case "ground":  // impact au sol
                    go = FirePool.Get(impactPrefab, new Vector3(0, 0.02f, 4f), Quaternion.LookRotation(Vector3.up));
                    camPos = new Vector3(5f, 3.2f, -2.5f); camLook = new Vector3(0, 1.6f, 4f);
                    times = new[] { 0.05f, 0.15f, 0.4f, 1f, 2.5f, 6f }; break;
                case "wall":    // impact sur le mur z=15
                    go = FirePool.Get(impactPrefab, new Vector3(0, 2.2f, 14.9f), Quaternion.LookRotation(Vector3.back));
                    camPos = new Vector3(6f, 3.4f, 7f); camLook = new Vector3(0, 2.6f, 14.9f);
                    times = new[] { 0.3f, 1f }; break;
                case "ceiling": // impact au plafond (slab y=4.2, zone x -20..-8)
                    go = FirePool.Get(impactPrefab, new Vector3(-12f, 4.18f, 4f), Quaternion.LookRotation(Vector3.down));
                    camPos = new Vector3(-6f, 1.6f, -1f); camLook = new Vector3(-12f, 3.2f, 4f);
                    times = new[] { 0.3f, 1f }; break;
                default:        // vol : front / 3q / dos
                    var p = Instantiate(projectilePrefab, new Vector3(0, 2.4f, -6f), Quaternion.identity);
                    foreach (var h in hide ?? new string[0]) { var ch = p.transform.Find(h); if (ch) ch.gameObject.SetActive(false); }
                    var fp = p.GetComponent<FireProjectile>(); fp.speed = 7f; fp.homing = 0f; fp.lifetime = 30f;
                    follow = p.transform; go = p;
                    switch (scenario) { case "flight_front": followOffset = new Vector3(0.4f, 0.2f, 3.2f); break; case "flight_34": followOffset = new Vector3(2.4f, 0.8f, 2.4f); break; default: followOffset = new Vector3(0.3f, 0.5f, -3.4f); break; }
                    times = new[] { 1.2f, 1.6f }; break;
            }

            float t = 0f; int shot = 0; bool followLive = follow != null; Vector3 lastFollow = Vector3.zero;
            while (shot < times.Length)
            {
                yield return null;
                t += Time.deltaTime;
                if (followLive && follow == null) { Debug.LogWarning("[FireVFX] projectile détruit avant la capture à t=" + t); followLive = false; }
                if (followLive) { lastFollow = follow.position; }
                if (follow || (!followLive && lastFollow != Vector3.zero)) { cam.transform.position = lastFollow + followOffset; cam.transform.LookAt(lastFollow); }
                else { cam.transform.position = camPos; cam.transform.LookAt(camLook); }
                if (t + 0.0001f >= times[shot])
                {
                    string fn = Path.Combine(outDir, tag + "_" + scenario + "_" + times[shot].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                    Grab(fn);
                    shot++;
                }
            }
            yield return null; yield return null;
            Time.captureDeltaTime = 0f;
            FireSettings.feedback = true;
            if (follow) Destroy(follow.gameObject);
            busy = false;
        }
    }
}
