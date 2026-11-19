// Anneau de zone posé à plat : un simple quad horizontal légèrement au-dessus du sol (voir ZoneFxController).
// Ne suit pas le relief : pour un terrain accidenté, utilise ZoneFx/RingProjected.
Shader "ZoneFx/RingFlat"
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
            Name "ZoneRingFlat"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ZoneRingCore.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return ShadeRing(i.uv);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
