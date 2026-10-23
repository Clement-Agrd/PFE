Shader "FireVFX/FireParticle"
{
    // Particule de flamme : le canal ALPHA de la couleur de particule = "vie" (1 → 0).
    // La chaleur = masque × bruit × vie ; un seuil la dissout depuis les bords, la rampe donne blanc → jaune → orange → rouge.
    // Texture : R = bruit tileable, G = masque de bouffée.
    Properties
    {
        _MainTex ("Noise (R) / Mask (G)", 2D) = "white" {}
        [HDR] _CoreColor ("Core (hot)", Color) = (4.0, 3.0, 1.6, 1)
        [HDR] _MidColor ("Mid", Color) = (3.0, 1.0, 0.12, 1)
        [HDR] _EdgeColor ("Edge (cool)", Color) = (1.1, 0.14, 0.02, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Cut ("Cut", Range(0, 1)) = 0.22
        _Soft ("Softness", Range(0.01, 0.6)) = 0.22
        _Scroll ("Flame Scroll", Range(0, 2)) = 0.5
        _SoftDist ("Soft Particle Distance", Range(0.01, 3)) = 0.7
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _CoreColor; half4 _MidColor; half4 _EdgeColor;
                half _Intensity; half _Cut; half _Soft; half _Scroll; half _SoftDist;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float eye : TEXCOORD1;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.color = i.color;
                o.eye = -TransformWorldToView(wp).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float life = saturate(i.color.a);
                float2 uv = i.uv;
                half2 m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rg;
                // seconde lecture du bruit qui défile vers le haut : l'intérieur "coule"
                half n2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * 1.7 + float2(0.37, -(1.0 - life) * _Scroll * 2.0)).r;
                float noise = lerp(m.r, n2, 0.5);
                float heat = m.g * (0.35 + noise * 1.25) * (0.25 + life * 0.95);

                half3 col = lerp(_EdgeColor.rgb, _MidColor.rgb, smoothstep(0.1, 0.4, heat));
                col = lerp(col, _CoreColor.rgb, smoothstep(0.4, 0.85, heat));
                float a = smoothstep(_Cut, _Cut + _Soft, heat);

                float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                float sd = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                a *= saturate((sd - i.eye) / _SoftDist);

                col *= i.color.rgb * _Intensity;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
