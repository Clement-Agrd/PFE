// Flamme réaliste : flipbook de chaleur coloré par une rampe, érodé selon l'âge de la particule, additif HDR (-> bloom).
Shader "StudioFx/Fire"
{
    Properties
    {
        _MainTex ("Flipbook de chaleur (R)", 2D) = "black" {}
        _RampTex ("Rampe chaleur -> couleur", 2D) = "white" {}
        _HdrIntensity ("Intensité HDR (bloom)", Range(0, 12)) = 3.0
        _Erode ("Érosion avec l'âge", Range(0, 1)) = 0.55
        _SoftDistance ("Fondu avec la géométrie (m)", Range(0.01, 2)) = 0.2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "StudioFire"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex StudioVert
            #pragma fragment frag
            #pragma target 3.0
            #include "StudioFlipbook.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_RampTex);  SAMPLER(sampler_RampTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _HdrIntensity;
                float  _Erode;
                float  _SoftDistance;
            CBUFFER_END

            half4 frag(StudioVaryings i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).r;
                float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw).r;
                float tex = lerp(a, b, i.blend);

                // Érosion : en fin de vie (alpha de la particule faible) seul le coeur le plus chaud reste.
                float age = 1.0 - i.color.a;
                float heat = saturate((tex - age * _Erode) / max(1.0 - age * _Erode, 0.05));

                // Fondu d'entrée / sortie : sans lui, le coeur le plus chaud apparaîtrait d'un coup à la naissance de la particule.
                float fade = smoothstep(0.0, 0.6, i.color.a);

                float3 col = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(heat, 0.5)).rgb;
                col *= i.color.rgb * _HdrIntensity * fade * StudioSoftFade(i.ndc, _SoftDistance);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
