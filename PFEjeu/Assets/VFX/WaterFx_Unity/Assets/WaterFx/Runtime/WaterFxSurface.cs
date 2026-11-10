using UnityEngine;

namespace WaterFx
{
    /// <summary>
    /// Poignée de contrôle d'une surface d'eau (un prefab = une surface). Permet de changer l'état de l'eau en jeu
    /// (tempête, crue, pollution...) sans toucher au matériau partagé : les valeurs passent par un MaterialPropertyBlock.
    ///
    /// Water_xxx (prefab)
    /// └── MeshRenderer (WaterFx/Water) + WaterFxSurface
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class WaterFxSurface : MonoBehaviour
    {
        private static readonly int WaveScaleId = Shader.PropertyToID("_WaveScale");
        private static readonly int TurbidityId = Shader.PropertyToID("_Turbidity");
        private static readonly int FoamAmountId = Shader.PropertyToID("_FoamAmount");
        private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");

        private MeshRenderer surfaceRenderer;
        private MaterialPropertyBlock block;

        #region Cycle de vie Unity
        private void Awake()
        {
            surfaceRenderer = GetComponent<MeshRenderer>();
            block = new MaterialPropertyBlock();
        }
        #endregion

        #region API publique
        /// <summary>Amplitude des vagues (0 = miroir, 1 = réglage du préréglage, 2 = tempête).</summary>
        public void SetWaveScale(float value) => Apply(WaveScaleId, Mathf.Clamp(value, 0f, 2f));

        /// <summary>Turbidité : multiplie l'absorption (eau claire -> boueuse).</summary>
        public void SetTurbidity(float value) => Apply(TurbidityId, Mathf.Clamp(value, 0f, 4f));

        /// <summary>Quantité d'écume (rivage + crêtes).</summary>
        public void SetFoamAmount(float value) => Apply(FoamAmountId, Mathf.Clamp(value, 0f, 1.5f));

        /// <summary>Vitesse du courant en m/s (rivières).</summary>
        public void SetFlowSpeed(float value) => Apply(FlowSpeedId, Mathf.Clamp(value, 0f, 2f));
        #endregion

        private void Apply(int propertyId, float value)
        {
            if (surfaceRenderer == null) Awake();
            surfaceRenderer.GetPropertyBlock(block);
            block.SetFloat(propertyId, value);
            surfaceRenderer.SetPropertyBlock(block);
        }
    }
}
