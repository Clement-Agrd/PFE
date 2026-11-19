// Particule d'ambiance (URP) : couleur = vertex color du Particle System, forme = canal R de la texture.
// Additif (feu, braises, lucioles...) ou alpha classique (fumée, neige) selon les propriétés de blend du matériau.
Shader "ZoneFx/Particle"
{
    Properties
    {
        _MainTex ("Masque (canal R)", 2D) = "white" {}
        _Intensity ("Intensité", Range(0, 4)) = 1.0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Blend source", Float) = 5      // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Blend destination", Float) = 10 // OneMinusSrcAlpha (1 = additif)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ZoneParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _Intensity;
                float  _SrcBlend;
                float  _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                return half4(i.color.rgb * _Intensity, mask * i.color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
