using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FireVFX
{
    public enum FireQuality { Low, Medium, High }

    public static class FireSettings
    {
        public static FireQuality quality = FireQuality.High;
        /// Faux pendant les captures déterministes : coupe hit stop et shake.
        public static bool feedback = true;
    }

    /// Lueur du feu la plus forte de la frame, publiée en globales shader (_FireLightPos / _FireLightColor)
    /// pour que la fumée soit éclairée par les flammes.
    public static class FireLights
    {
        class Runner : MonoBehaviour { void LateUpdate() { FireLights.EndFrame(); } }
        static Runner runner; static int frame = -1; static float best; static bool dirty;
        static readonly int PosId = Shader.PropertyToID("_FireLightPos"), ColId = Shader.PropertyToID("_FireLightColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { runner = null; frame = -1; Shader.SetGlobalVector(PosId, new Vector4(0, -999, 0, 1)); Shader.SetGlobalColor(ColId, Color.black); }

        public static void Report(Vector3 pos, Color color, float intensity, float range)
        {
            if (runner == null) { var g = new GameObject("FireLights") { hideFlags = HideFlags.HideAndDontSave }; runner = g.AddComponent<Runner>(); }
            if (frame != Time.frameCount) { frame = Time.frameCount; best = -1f; }
            if (intensity <= best) return;
            best = intensity; dirty = true;
            Shader.SetGlobalVector(PosId, new Vector4(pos.x, pos.y, pos.z, Mathf.Max(range, 0.1f)));
            Shader.SetGlobalColor(ColId, new Color(color.r, color.g, color.b, 1f) * (intensity * 0.12f));
        }

        static void EndFrame()
        {
            if (frame != Time.frameCount && dirty) { dirty = false; Shader.SetGlobalColor(ColId, Color.black); }
        }
    }


    /// Pool simple d'instances par prefab, sans allocation par frame une fois chauffé.
    public static class FirePool
    {
        class Tag : MonoBehaviour { public GameObject prefab; }
        static readonly Dictionary<GameObject, Stack<GameObject>> pools = new Dictionary<GameObject, Stack<GameObject>>();

        public static GameObject Get(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            if (!pools.TryGetValue(prefab, out var st)) pools[prefab] = st = new Stack<GameObject>();
            GameObject go = null;
            while (st.Count > 0 && go == null) go = st.Pop();
            if (go == null) { go = Object.Instantiate(prefab); go.AddComponent<Tag>().prefab = prefab; }
            go.transform.SetPositionAndRotation(pos, rot);
            go.SetActive(true);
            return go;
        }

        public static void Release(GameObject go)
        {
            var t = go.GetComponent<Tag>();
            if (t == null) { Object.Destroy(go); return; }
            go.SetActive(false);
            pools[t.prefab].Push(go);
        }
    }




}
