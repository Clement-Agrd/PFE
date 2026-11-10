// Braise / étincelle / halo : sprite doux additif HDR (-> bloom), couleur = couleur de la particule, particules "soft".
Shader "StudioFx/Glow"
{
    Properties
    {
        _MainTex ("Masque (R)", 2D) = "white" {}
        _HdrIntensity ("Intensité HDR (bloom)", Range(0, 16)) = 6.0
        _SoftDistance ("Fondu avec la géométrie (m)", Range(0.01, 2)) = 0.05
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+2" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "StudioGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
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
                float  _HdrIntensity;
                float  _SoftDistance;
            CBUFFER_END

            half4 frag(StudioVaryings i) : SV_Target
            {
                float mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).r;
                float fade = StudioSoftFade(i.ndc, _SoftDistance);
                return half4(i.color.rgb * _HdrIntensity, mask * i.color.a * fade);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
