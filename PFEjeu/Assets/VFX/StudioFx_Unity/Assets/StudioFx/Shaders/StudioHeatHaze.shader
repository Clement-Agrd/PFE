// Distorsion de chaleur : déforme l'image de la scène derrière la particule (nécessite "Opaque Texture" dans l'asset URP).
Shader "StudioFx/HeatHaze"
{
    Properties
    {
        _MainTex ("Flipbook fumée (R, G)", 2D) = "black" {}
        _Strength ("Force de la distorsion", Range(0, 0.15)) = 0.03
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "StudioHeatHaze"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex StudioVert
            #pragma fragment frag
            #pragma target 3.0
            #include "StudioFlipbook.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _Strength;
            CBUFFER_END

            half4 frag(StudioVaryings i) : SV_Target
            {
                float2 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).rg;
                float2 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw).rg;
                float2 t = lerp(a, b, i.blend);

                float mask = saturate(t.x * 3.0) * i.color.a;
                float2 offset = float2(t.y - 0.5, t.x - 0.5) * _Strength * i.color.a;
                float2 screenUV = i.ndc.xy / i.ndc.w;
                float3 scene = SampleSceneColor(screenUV + offset);
                return half4(scene, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
