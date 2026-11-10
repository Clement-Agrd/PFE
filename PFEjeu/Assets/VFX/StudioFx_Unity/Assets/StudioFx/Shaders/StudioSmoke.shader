// Fumée réaliste : flipbook densité (R) + éclairage cuit (G), éclairée par la lumière principale, l'ambiance et une lueur de foyer.
Shader "StudioFx/Smoke"
{
    Properties
    {
        _MainTex ("Flipbook fumée (R densité, G éclairage)", 2D) = "black" {}
        _TintColor ("Teinte de la fumée", Color) = (0.30, 0.29, 0.29, 1)
        _AmbientColor ("Lumière ambiante", Color) = (0.09, 0.10, 0.14, 1)
        _LightAmount ("Part de la lumière principale", Range(0, 2)) = 0.4
        [HDR] _GlowColor ("Lueur du foyer (côté éclairé)", Color) = (0.5, 0.2, 0.05, 1)
        _Opacity ("Opacité", Range(0, 3)) = 1.2
        _SoftDistance ("Fondu avec la géométrie (m)", Range(0.01, 3)) = 0.7
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "StudioSmoke"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex StudioVert
            #pragma fragment frag
            #pragma target 3.0
            #include "StudioFlipbook.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TintColor;
                float4 _AmbientColor;
                float  _LightAmount;
                float4 _GlowColor;
                float  _Opacity;
                float  _SoftDistance;
            CBUFFER_END

            half4 frag(StudioVaryings i) : SV_Target
            {
                float2 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).rg;
                float2 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw).rg;
                float2 t = lerp(a, b, i.blend);          // x = densité, y = éclairage cuit

                float3 lighting = _AmbientColor.rgb
                                + _MainLightColor.rgb * _LightAmount * t.y
                                + _GlowColor.rgb * t.y;
                float3 col = _TintColor.rgb * i.color.rgb * lighting;
                float alpha = saturate(t.x * i.color.a * _Opacity) * StudioSoftFade(i.ndc, _SoftDistance);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
