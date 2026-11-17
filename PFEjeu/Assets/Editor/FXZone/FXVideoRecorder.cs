#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace Core.FXZone.Editor
{
    /// <summary>
    /// Filme chaque baie FX en MP4 (H.264) en Play mode, via le FXViewer. Capture image par image à cadence
    /// fixe (Time.captureFramerate), sans HUD ni joueur. Appel : FXVideoRecorder.Begin(dossier).
    /// </summary>
    public static class FXVideoRecorder
    {
        private struct Job { public string name; public int index; public float duration; }

        private enum Phase { Idle, Settle, Record }

        private static Phase phase;
        private static List<Job> jobs;
        private static int current;
        private static float timer;
        private static int fps, width, height;
        private static string outDir;
        private static FXViewer viewer;
        private static MediaEncoder encoder;
        private static RenderTexture rt;
        private static Texture2D tex;
        private static Camera cam;

        public static bool IsRunning => phase != Phase.Idle;
        public static string Status = "idle";

        public static void Begin(string dir, int w = 1920, int h = 1080, int frameRate = 30)
        {
            if (IsRunning) return;
            viewer = Object.FindAnyObjectByType<FXViewer>();
            if (viewer == null || !Application.isPlaying) { Status = "erreur: Play mode + FXViewer requis"; return; }
            Directory.CreateDirectory(dir);
            outDir = dir; width = w; height = h; fps = frameRate;

            viewer.Enter();
            cam = Camera.main;
            jobs = new List<Job>();
            for (int i = 0; i < viewer.BayCount; i++)
            {
                string n = viewer.BayName(i);
                float dur = n == "Portail" ? 13f : n == "Desintegration" ? 14f : 8f;
                jobs.Add(new Job { name = n, index = i, duration = dur });
            }

            rt = new RenderTexture(width, height, 24);
            tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Time.captureFramerate = fps;
            current = 0;
            StartSettle();
            EditorApplication.update += Tick;
        }

        private static void StartSettle()
        {
            viewer.Select(jobs[current].index);
            timer = 2.5f;
            phase = Phase.Settle;
            Status = $"cadrage {jobs[current].name} ({current + 1}/{jobs.Count})";
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { Finish("interrompu (Play mode quitté)"); return; }
            float dt = 1f / fps;

            if (phase == Phase.Settle)
            {
                timer -= dt;
                if (timer > 0f) return;
                string path = Path.Combine(outDir, "FX_" + jobs[current].name + ".mp4");
                if (File.Exists(path)) File.Delete(path);
                var h264 = new H264EncoderAttributes
                {
                    gopSize = (uint)fps,
                    numConsecutiveBFrames = 2,
                    profile = VideoEncodingProfile.H264High
                };
                var vattr = new VideoTrackEncoderAttributes(h264)
                {
                    frameRate = new MediaRational(fps),
                    width = (uint)width,
                    height = (uint)height,
                    targetBitRate = 18000000
                };
                encoder = new MediaEncoder(path, vattr);
                timer = 0f;
                phase = Phase.Record;
                Status = $"enregistre {jobs[current].name}";
                return;
            }

            if (phase == Phase.Record)
            {
                var prevTarget = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;

                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prevActive;
                encoder.AddFrame(tex);

                timer += dt;
                if (timer >= jobs[current].duration)
                {
                    encoder.Dispose();
                    encoder = null;
                    current++;
                    if (current >= jobs.Count) { Finish("termine"); return; }
                    StartSettle();
                }
            }
        }

        private static void Finish(string status)
        {
            EditorApplication.update -= Tick;
            if (encoder != null) { encoder.Dispose(); encoder = null; }
            Time.captureFramerate = 0;
            if (viewer != null) viewer.Exit();
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (tex != null) Object.DestroyImmediate(tex);
            phase = Phase.Idle;
            Status = status;
        }
    }
}
#endif
