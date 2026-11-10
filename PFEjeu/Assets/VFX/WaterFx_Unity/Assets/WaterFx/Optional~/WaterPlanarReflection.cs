// OPTIONNEL — non compilé par Unity tant que le dossier s'appelle "Optional~".
// Pour l'activer : renomme le dossier en "Optional" (sans ~), puis coche "Reflexion planaire" sur le matériau d'eau.
// [Guessing] L'API de rendu d'une caméra secondaire diffère entre Unity 2022 et Unity 6 : branche 6000 non testée.
// Coût : un rendu de scène supplémentaire par frame (résolution réduite par défaut).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterFx
{
    /// <summary>
    /// Réflexion planaire : rend la scène vue en miroir par rapport au plan d'eau dans une RenderTexture globale
    /// (_WaterPlanarReflection) lue par le shader WaterFx/Water quand le mot-clé _PLANAR_REFLECTION est actif.
    /// À poser sur le prefab d'eau (un seul par scène en pratique).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterPlanarReflection : MonoBehaviour
    {
        private static readonly int ReflectionTextureId = Shader.PropertyToID("_WaterPlanarReflection");

        [SerializeField] private LayerMask reflectedLayers = ~0;
        [SerializeField, Range(0.25f, 1f)] private float resolutionScale = 0.5f;
        [SerializeField] private float clipPlaneOffset = 0.05f;

        private Camera reflectionCamera;
        private RenderTexture reflectionTexture;
        private static bool isRendering;      // évite la récursion (la caméra miroir déclenche beginCameraRendering)

        #region Cycle de vie Unity
        private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            Release();
        }
        #endregion

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (isRendering || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview) return;

            isRendering = true;
            try
            {
                EnsureResources(camera);
                SyncCamera(camera);

                Vector3 planePos = transform.position;
                Vector3 normal = Vector3.up;
                float d = -Vector3.Dot(normal, planePos) - clipPlaneOffset;

                Matrix4x4 reflection = CalculateReflectionMatrix(new Vector4(normal.x, normal.y, normal.z, d));
                reflectionCamera.worldToCameraMatrix = camera.worldToCameraMatrix * reflection;
                Vector4 clipPlane = CameraSpacePlane(reflectionCamera, planePos, normal);
                reflectionCamera.projectionMatrix = camera.CalculateObliqueMatrix(clipPlane);
                reflectionCamera.cullingMatrix = camera.projectionMatrix * camera.worldToCameraMatrix;

                GL.invertCulling = true;
            #if UNITY_6000_0_OR_NEWER
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = reflectionTexture };
                if (RenderPipeline.SupportsRenderRequest(reflectionCamera, request))
                    RenderPipeline.SubmitRenderRequest(reflectionCamera, request);
            #else
                reflectionCamera.targetTexture = reflectionTexture;
                UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
            #endif
                GL.invertCulling = false;

                Shader.SetGlobalTexture(ReflectionTextureId, reflectionTexture);
            }
            finally
            {
                isRendering = false;
            }
        }

        #region Ressources
        private void EnsureResources(Camera source)
        {
            int w = Mathf.Max(16, Mathf.RoundToInt(source.pixelWidth * resolutionScale));
            int h = Mathf.Max(16, Mathf.RoundToInt(source.pixelHeight * resolutionScale));

            if (reflectionTexture == null || reflectionTexture.width != w || reflectionTexture.height != h)
            {
                if (reflectionTexture != null) reflectionTexture.Release();
                reflectionTexture = new RenderTexture(w, h, 16, RenderTextureFormat.DefaultHDR) { name = "WaterPlanarReflection" };
            }

            if (reflectionCamera == null)
            {
                var go = new GameObject("WaterReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
                reflectionCamera = go.AddComponent<Camera>();
                reflectionCamera.enabled = false;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderShadows = false;
            }
        }

        private void SyncCamera(Camera source)
        {
            reflectionCamera.CopyFrom(source);
            reflectionCamera.cameraType = CameraType.Reflection;
            reflectionCamera.cullingMask = reflectedLayers;
            reflectionCamera.enabled = false;
        }

        private void Release()
        {
            if (reflectionTexture != null) { reflectionTexture.Release(); reflectionTexture = null; }
            if (reflectionCamera != null) { DestroyImmediate(reflectionCamera.gameObject); reflectionCamera = null; }
        }
        #endregion

        #region Maths de réflexion
        private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal)
        {
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 offsetPos = pos + normal * clipPlaneOffset;
            Vector3 cpos = m.MultiplyPoint(offsetPos);
            Vector3 cnormal = m.MultiplyVector(normal).normalized;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }

        private static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = 1f - 2f * plane.x * plane.x; m.m01 = -2f * plane.x * plane.y; m.m02 = -2f * plane.x * plane.z; m.m03 = -2f * plane.w * plane.x;
            m.m10 = -2f * plane.y * plane.x; m.m11 = 1f - 2f * plane.y * plane.y; m.m12 = -2f * plane.y * plane.z; m.m13 = -2f * plane.w * plane.y;
            m.m20 = -2f * plane.z * plane.x; m.m21 = -2f * plane.z * plane.y; m.m22 = 1f - 2f * plane.z * plane.z; m.m23 = -2f * plane.w * plane.z;
            return m;
        }
        #endregion
    }
}
