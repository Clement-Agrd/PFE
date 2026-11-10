// Sol de la zone de soin (URP, additif).
// Mesh = quad XZ unité (-0.5..0.5, UV 0..1) mis à l'échelle (2 x rayon) par HealZoneFx.
// Toutes les maths (disque, anneaux, croix, ondes, runes) vivent dans HealZoneCore.hlsl (partagé avec l'aperçu web).
// v2 : rampe de couleur HDR (cœur -> bords), bande de runes (atlas généré par script), bord qui pulse, onde d'apparition,
//      fondu global _Fade (apparition / dissipation) et temps propre _HealTime (vitesse réglable sans à-coup).
Shader "HealFx/ZoneGround"
{
    Properties
    {
        _ColorA ("Couleur principale (bords, anneaux, ondes)", Color) = (0.30, 1.00, 0.50, 1)
        _ColorB ("Couleur secondaire (croix, runes)", Color) = (1.00, 0.88, 0.45, 1)
        [HDR] _ColorCore ("Couleur du coeur (HDR)", Color) = (2.2, 1.9, 1.1, 1)
        _Intensity ("Intensité", Range(0, 4)) = 1.2
        _Pulse ("Progression du soin 0..1 (-1 = aucun) - piloté par script", Float) = -1
        _Pop ("Onde d'apparition 0..1 (-1 = aucune) - piloté par script", Float) = -1
        _Fade ("Fondu global 0..1 - piloté par script", Range(0, 1.5)) = 1
        _HealTime ("Temps de l'effet (s) - piloté par script", Float) = 0
        _EdgePulse ("Bord lumineux", Range(0, 2)) = 1
        _Quality ("Qualité 0 = Low, 1 = Med, 2 = High - piloté par script", Float) = 1
        [NoScaleOffset] _RuneAtlas ("Atlas de runes 4x4 (canal R)", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HealZoneGround"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1          // évite le z-fighting avec le sol

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HealZoneCore.hlsl"

            TEXTURE2D(_RuneAtlas);
            SAMPLER(sampler_RuneAtlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA;
                float4 _ColorB;
                float4 _ColorCore;
                float  _Intensity;
                float  _Pulse;
                float  _Pop;
                float  _Fade;
                float  _HealTime;
                float  _EdgePulse;
                float  _Quality;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 p          : TEXCOORD0;   // -1..1 ; (0,0) = centre de la zone
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.p = (input.uv - 0.5) * 2.0;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _HealTime;
                float r = length(i.p);
                float3 layers = HealZoneLayersEx(i.p, t, _Pulse, _Pop, _EdgePulse);

                // rampe cœur chaud -> bords, jamais une couleur plate
                float3 ramp = HealRamp(r, _ColorCore.rgb, _ColorA.rgb);
                float3 col = ramp * layers.x
                           + _ColorB.rgb * layers.y
                           + lerp(_ColorA.rgb, float3(1.0, 1.0, 1.0), 0.35) * layers.z;

                // runes : désactivées en qualité Low (une lecture de texture en moins par pixel)
                if (_Quality > 0.5)
                {
                    float4 rune = HealRuneUV(i.p, t);
                    float glyph = SAMPLE_TEXTURE2D_LOD(_RuneAtlas, sampler_RuneAtlas, rune.xy, 0).r * rune.z;
                    float twinkle = 0.7 + 0.3 * sin(t * 3.0 + rune.w * 1.7);
                    float edge = 1.0 - smoothstep(0.97, 1.0, r);
                    col += lerp(_ColorB.rgb, _ColorCore.rgb, 0.4) * glyph * twinkle * 1.4 * edge;
                }

                return half4(col * _Intensity * _Fade, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
