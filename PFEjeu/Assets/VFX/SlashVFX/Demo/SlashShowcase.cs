using System.Collections;
using UnityEngine;

namespace SlashVFX.Demo
{
    /// <summary>Loops the four hits with a slow orbiting camera (Tools/Slash VFX/Showcase).</summary>
    [AddComponentMenu("Slash VFX/Showcase")]
    public sealed class SlashShowcase : MonoBehaviour
    {
        public const string PrefKey = "SlashVFX.Showcase";

        public SlashComboController controller;
        public Camera cam;
        public Transform pivot;
        [Min(0.05f)] public float orbitSpeed = 8f;
        [Min(1f)] public float radius = 5.2f;
        public float height = 2.1f;
        public bool autoStart;

        float angle = 180f;
        bool running;

        void Start()
        {
            if (PlayerPrefs.GetInt(PrefKey, 0) == 1)
            {
                PlayerPrefs.SetInt(PrefKey, 0);
                autoStart = true;
            }
            if (autoStart) StartShowcase();
        }

        public void StartShowcase()
        {
            if (running || controller == null) return;
            running = true;
            StartCoroutine(Loop());
        }

        IEnumerator Loop()
        {
            controller.readMouse = false;
            yield return new WaitForSeconds(0.6f);
            while (true)
            {
                for (int i = 0; i < controller.steps.Length; i++)
                {
                    controller.PlayStep(i);
                    yield return new WaitForSeconds(controller.steps[i].duration + (i == controller.steps.Length - 1 ? 1.4f : 0.22f));
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        void LateUpdate()
        {
            if (!running || cam == null || pivot == null) return;
            angle += orbitSpeed * Time.unscaledDeltaTime;
            float a = angle * Mathf.Deg2Rad;
            Vector3 target = pivot.position + new Vector3(0f, -0.1f, 1.2f);
            Vector3 pos = target + new Vector3(Mathf.Sin(a) * radius * 0.45f, height - 0.8f, Mathf.Cos(a) * radius);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
        }
    }
}
