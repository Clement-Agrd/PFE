Shader "FireVFX/FireDistortion"
{
    // Distorsion de chaleur / onde de choc. Requis : URP Asset > Opaque Texture.
    Properties
    {
        _MainTex ("Noise (R)", 2D) = "gray" {}
        _MaskTex ("Mask (A)", 2D) = "white" {}
        _Strength ("Strength", Range(0, 0.2)) = 0.03
        _Tiling ("Noise Tiling", Range(0.2, 6)) = 1.5
        _Speed ("Scroll Speed", Range(0, 4)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float4 _MaskTex_ST;
                half _Strength; half _Tiling; half _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv; o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                half n1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _Tiling + float2(0, -t)).r;
                half n2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.yx * _Tiling * 1.3 + float2(t * 0.7, 0.31)).r;
                half mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv).a * i.color.a;
                float2 off = (float2(n1, n2) - 0.5) * 2.0 * _Strength * mask;
                float2 suv = saturate(i.positionCS.xy / _ScaledScreenParams.xy + off);
                return half4(SampleSceneColor(suv), saturate(mask * 4.0));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
