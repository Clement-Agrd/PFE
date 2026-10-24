Shader "VFX/DisintegrateParticle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Tint ("Tint (HDR)", Color) = (1,1,1,1)
        [HDR] _HotColor ("Hot Color (HDR)", Color) = (6,2.5,0.6,1)
        _Flipbook ("Flipbook (cols, rows)", Vector) = (1,1,0,0)
        [Enum(Alpha,0,Additive,1)] _Add ("Additive", Float) = 0
        // 0 = texture * couleur ; 1 = eclat de papier (bord incandescent, dissolution par bruit)
        [Enum(Standard,0,Scrap,1)] _Mode ("Mode", Float) = 0
        [HideInInspector] _SrcBlend ("__src", Float) = 5
        [HideInInspector] _DstBlend ("__dst", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half4 _HotColor;
                float4 _Flipbook;
                float _Add;
                float _Mode;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vert(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.uv = i.uv;
                o.col = i.col;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.uv;
                if (_Flipbook.x > 1.0)
                {
                    // le module Texture Sheet Animation ecrit deja dans uv ; ici, simple recadrage.
                    uv = i.uv;
                }
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                if (_Mode > 0.5)
                {
                    // r = chaleur, a = vie ; uv.y = distance au centre (0 centre -> 1 bord)
                    half heat = i.col.r;
                    half life = i.col.a;
                    half n = t.g;
                    clip(n - (1.0 - life) * 1.05 + 0.02 * 0);
                    half edge = smoothstep(0.55, 1.0, uv.y);
                    half3 c = _Tint.rgb * (0.6 + 0.8 * t.b) + _HotColor.rgb * edge * heat;
                    c += _HotColor.rgb * 0.15 * heat * (1 - edge);
                    return half4(c, 1);
                }
                half4 c = t * _Tint * half4(i.col.rgb, 1);
                c.a *= i.col.a;
                if (_Add > 0.5) c.rgb *= c.a;
                return c;
            }
            ENDHLSL
        }
    }
}
