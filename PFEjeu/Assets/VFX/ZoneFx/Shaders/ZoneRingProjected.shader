// Anneau de zone PROJETÉ sur le décor (type décalque) : un volume (cube) dont le shader relit la profondeur de la scène
// et reconstruit la position monde ; l'anneau épouse donc le relief (pentes, marches, terrain bosselé).
// Besoin : "Depth Texture" activé dans l'asset URP. Dessine aussi sur les objets qui traversent le volume (personnages, tour).
Shader "ZoneFx/RingProjected"
{
    Properties
    {
        [HDR] _Color ("Couleur de l'anneau (HDR)", Color) = (0.4, 1.0, 0.25, 1)
        [HDR] _CoreColor ("Couleur du trait (HDR)", Color) = (0.8, 1.0, 0.7, 1)
        _RingRadius ("Rayon de l'anneau (relatif)", Range(0.3, 0.95)) = 0.8
        _RingWidth ("Épaisseur du trait", Range(0.002, 0.05)) = 0.010
        _GlowWidth ("Étalement du halo", Range(0.005, 0.2)) = 0.045
        _GlowIntensity ("Intensité du halo", Range(0, 2)) = 0.55
        _FillAlpha ("Voile intérieur", Range(0, 1)) = 0.16
        _DashCount ("Tirets (0 = continu)", Range(0, 24)) = 0
        _DashRatio ("Part allumée des tirets", Range(0.1, 1)) = 0.8
        _DashSpeed ("Rotation des tirets", Range(-1, 1)) = 0.04
        _PulseAmount ("Respiration du rayon", Range(0, 0.05)) = 0.004
        _PulseSpeed ("Vitesse de respiration", Range(0, 6)) = 1.6
        _SwirlSpeed ("Rotation du voile", Range(-1, 1)) = 0.15
        _Visible ("Visibilité (script)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ZoneRingProjected"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always            // le test de profondeur est fait à la main (relecture de la depth texture)
            Cull Front              // on dessine l'intérieur du cube : ça marche même quand la caméra est dedans

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "ZoneRingCore.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(screenUV);
                float3 positionWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                float3 positionOS = TransformWorldToObject(positionWS);

                clip(0.5 - abs(positionOS));             // hors du cube : rien
                return ShadeRing(positionOS.xz + 0.5);   // le dessus du cube (plan XZ) sert de carte
            }
            ENDHLSL
        }
    }

    Fallback Off
}
