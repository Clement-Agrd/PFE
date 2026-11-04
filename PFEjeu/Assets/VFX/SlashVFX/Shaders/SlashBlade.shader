// Slash blade: scrolled streak mask, noise-threshold dissolve (tail eats from U=0 to U=1),
// swept reveal head, hot dissolve edge, HDR colour from a 1D ramp. Additive, double sided.
// Driven per renderer through MaterialPropertyBlock (_Reveal, _Dissolve, _Intensity, _Ramp, _MainTex ...).
Shader "VFX/SlashBlade"
{
    Properties
    {
        [MainTexture] _MainTex("Slash Mask (R)", 2D) = "white" {}
        _NoiseTex("Noise (R)", 2D) = "gray" {}
        _Ramp("Ramp 256x1 (colour by intensity)", 2D) = "white" {}
        _Tint("Tint (rgb) / Alpha", Color) = (1, 1, 1, 1)
        _Reveal("Reveal (head of the sweep)", Range(0, 1)) = 1
        _Dissolve("Dissolve (tail)", Range(0, 1.2)) = 0
        _Intensity("Intensity (HDR)", Range(0, 20)) = 4
        _Edge("Hot Edge Width", Range(0.005, 0.5)) = 0.08
        _Scroll("Scroll", Float) = 0
        _TilingU("Mask Tiling U", Float) = 1
        _NoiseTile("Noise Tiling", Vector) = (3, 1, 0, 0)
        _HeadSoft("Head Softness", Range(0.01, 0.4)) = 0.1
        _EdgeFade("V Edge Fade", Range(0.01, 0.5)) = 0.22
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SlashForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_Ramp);     SAMPLER(sampler_Ramp);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float _Reveal;
                float _Dissolve;
                float _Intensity;
                float _Edge;
                float _Scroll;
                float _TilingU;
                float4 _NoiseTile;
                float _HeadSoft;
                float _EdgeFade;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float u = i.uv.x;
                float v = i.uv.y;

                float mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(u * _TilingU - _Scroll, v)).r;
                float nz = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * _NoiseTile.x - _Scroll * 0.5, v * _NoiseTile.y)).r;

                // dissolve front: noise biased by U so the tail (U=0) disappears first
                float field = lerp(nz, u, 0.55);
                float d = smoothstep(_Dissolve, _Dissolve + 0.1, field);
                float band = smoothstep(_Dissolve, _Dissolve + 0.02, field)
                           * (1.0 - smoothstep(_Dissolve + 0.02, _Dissolve + 0.02 + _Edge, field));

                float reveal = saturate((_Reveal - u) / _HeadSoft);
                float edgeFade = smoothstep(0.0, _EdgeFade, v) * smoothstep(0.0, _EdgeFade, 1.0 - v);
                float headBoost = 1.0 + 1.6 * saturate(1.0 - (_Reveal - u) / 0.22);

                float m = mask * d * reveal * edgeFade;
                float r = saturate(m * headBoost * 0.95);
                float3 col = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(r, 0.5)).rgb * _Intensity * headBoost;
                float hot = band * mask * reveal * edgeFade;
                col += SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(0.97, 0.5)).rgb * hot * _Intensity * 0.9;

                float alpha = saturate(m * 1.6 + hot) * _Tint.a;
                return half4(col * _Tint.rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
