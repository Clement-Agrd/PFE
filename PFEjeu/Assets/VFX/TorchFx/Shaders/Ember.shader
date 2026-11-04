// Braise : point lumineux rond, additif, HDR, qui lit la couleur du ParticleSystem (Color over Lifetime).
Shader "TorchFx/Ember"
{
    Properties
    {
        [HDR] _Color ("Tint (HDR)", Color) = (4.0, 1.6, 0.3, 1)
        _Softness ("Edge Softness", Range(0.05, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Ember"

            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float radius = length(IN.uv - 0.5) * 2.0;
                float dot01 = smoothstep(0.0, _Softness, saturate(1.0 - radius));
                float core = dot01 * dot01; // cœur plus lumineux que le bord

                half3 rgb = _Color.rgb * IN.color.rgb;
                half alpha = core * IN.color.a * _Color.a;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
