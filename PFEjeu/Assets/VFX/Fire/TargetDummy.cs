using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FireVFX
{
    /// Cible de test : encaisse les dégâts, clignote et rougeoie quand elle brûle.
    public class TargetDummy : MonoBehaviour
    {
        public float health = 200f;
        float hurt, burn;
        Renderer rend; MaterialPropertyBlock mpb;
        static readonly int Base = Shader.PropertyToID("_BaseColor");
        static readonly int Emis = Shader.PropertyToID("_EmissionColor");
        void Awake() { rend = GetComponent<Renderer>(); mpb = new MaterialPropertyBlock(); }
        public void TakeDamage(float d) { health -= d; hurt = 0.25f; }
        public void OnBurn(float t) { burn = t; }
        void Update()
        {
            hurt = Mathf.Max(0f, hurt - Time.deltaTime); burn = Mathf.Max(0f, burn - Time.deltaTime);
            if (!rend) return;
            rend.GetPropertyBlock(mpb);
            mpb.SetColor(Base, Color.Lerp(new Color(0.45f, 0.42f, 0.4f), Color.white, hurt * 4f));
            mpb.SetColor(Emis, new Color(1f, 0.25f, 0.04f) * Mathf.Clamp01(burn) * (0.6f + 0.4f * Mathf.PerlinNoise(Time.time * 8f, 0f)));
            rend.SetPropertyBlock(mpb);
        }
    }
}
