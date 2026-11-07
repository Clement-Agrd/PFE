using System.IO;
using UnityEditor;
using UnityEngine;

namespace Defense.Relief.EditorTools
{
    /// <summary>Captures PNG de controle : vue de dessus et vue du plateau vers la vallee.</summary>
    public static class DefenseReliefScreenshotTool
    {
        private const string OutputFolder = "DefenseRelief_Captures";
        private const int Width = 1600;
        private const int Height = 900;

        [MenuItem("Tools/Defense/Relief/7 - Captures de controle")]
        public static void Capture()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputFolder);
            Directory.CreateDirectory(dir);

            Vector3 herse = DefenseReliefScene.Require(DefenseReliefScene.HerseName).transform.position;
            Transform spawn = DefenseReliefScene.SpawnZones()[3];

            // Vue de dessus, centree entre la herse et le spawn le plus eloigne.
            Vector3 center = (herse + spawn.position) * 0.5f;
            Render(Path.Combine(dir, "top_view.png"), cam =>
            {
                cam.orthographic = true;
                cam.orthographicSize = 150f;
                cam.transform.position = new Vector3(center.x + 30f, 400f, center.z);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.farClipPlane = 800f;
            });

            // Vue depuis le plateau (au-dessus de la herse) vers la vallee / spawn 04.
            Vector3 eye = herse + new Vector3(0f, 10f, 0f);
            Vector3 look = spawn.position + Vector3.up * 3f;
            Render(Path.Combine(dir, "plateau_to_valley.png"), cam =>
            {
                cam.orthographic = false;
                cam.fieldOfView = 60f;
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
                cam.farClipPlane = 800f;
            });
            Debug.Log("[DefenseRelief] Captures ecrites dans " + dir);
        }

        private static void Render(string path, System.Action<Camera> setup)
        {
            var go = new GameObject("DefenseRelief_TempCamera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.targetTexture = rt;
                setup(cam);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }
    }
}
